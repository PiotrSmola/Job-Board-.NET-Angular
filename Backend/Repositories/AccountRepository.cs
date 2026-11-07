using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class AccountRepository : IAccountRepository
{
    private readonly ApplicationDbContext _context;

    public AccountRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<List<ProfileDocument>> GetDocumentsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.ProfileDocuments
            .AsNoTracking()
            .Where(d => d.UserId == userId)
            .ToListAsync(cancellationToken);
    }

    public async Task<ProfileDocument?> GetDocumentAsync(string userId, int documentId, CancellationToken cancellationToken = default)
    {
        return await _context.ProfileDocuments
            .AsNoTracking()
            .FirstOrDefaultAsync(d => d.Id == documentId && d.UserId == userId, cancellationToken);
    }

    public async Task<List<ProfileDocument>> GetDocumentsAsync(string userId, IReadOnlyCollection<int> documentIds,
        CancellationToken cancellationToken = default)
    {
        return await _context.ProfileDocuments
            .AsNoTracking()
            .Where(d => d.UserId == userId && documentIds.Contains(d.Id))
            .ToListAsync(cancellationToken);
    }

    public async Task<int> CountDocumentsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _context.ProfileDocuments.CountAsync(d => d.UserId == userId, cancellationToken);
    }

    public async Task<DocumentsAddResult> AddDocumentsAsync(string userId, IReadOnlyList<ProfileDocument> documents,
        CancellationToken cancellationToken = default)
    {
        // Limity sprawdzane pod blokadą X wiersza konta: równoległe uploady tego samego użytkownika czekają tu na siebie,
        // a każdy liczy już zatwierdzone dokumenty poprzednika. Tę samą blokadę bierze usunięcie konta, więc odczytane
        // tam klucze plików obejmują dokumenty zapisane tutaj.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        if (await LockUserAsync(userId, cancellationToken) == 0)
        {
            return DocumentsAddResult.UserNotFound;
        }

        var existing = await _context.ProfileDocuments
            .Where(d => d.UserId == userId)
            .GroupBy(d => d.UserId)
            .Select(g => new { Count = g.Count(), Size = g.Sum(d => d.Size) })
            .FirstOrDefaultAsync(cancellationToken);

        if ((existing?.Count ?? 0) + documents.Count > AttachmentFiles.MaxProfileDocuments)
        {
            return DocumentsAddResult.TooMany;
        }

        if ((existing?.Size ?? 0) + documents.Sum(d => d.Size) > AttachmentFiles.MaxProfileTotalSize)
        {
            return DocumentsAddResult.TotalTooLarge;
        }

        foreach (var document in documents)
        {
            document.UserId = userId;
        }

        _context.ProfileDocuments.AddRange(documents);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return DocumentsAddResult.Added;
    }

    public async Task<IReadOnlyList<string>> GetExistingDocumentFileKeysAsync(IReadOnlyCollection<string> fileKeys,
        CancellationToken cancellationToken = default)
    {
        if (fileKeys.Count == 0)
        {
            return [];
        }

        return await _context.ProfileDocuments
            .AsNoTracking()
            .Where(d => fileKeys.Contains(d.FileKey))
            .Select(d => d.FileKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<string?> DeleteDocumentAsync(string userId, int documentId, CancellationToken cancellationToken = default)
    {
        var fileKey = await _context.ProfileDocuments
            .Where(d => d.Id == documentId && d.UserId == userId)
            .Select(d => d.FileKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (fileKey == null)
        {
            return null;
        }

        // klucz dokumentu się nie zmienia; z równoległych usunięć tylko jedno usuwa wiersz (i plik)
        var deleted = await _context.ProfileDocuments
            .Where(d => d.Id == documentId && d.UserId == userId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? null : fileKey;
    }

    public async Task<AccountDeletion> DeleteAccountAsync(string userId)
    {
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // Najpierw wszystkie blokady (UPDATE bez zmiany wartości), dopiero potem odczyty: pierwszy zwykły SELECT
        // ustala migawkę transakcji, więc widzi już załączniki i dokumenty zatwierdzone przez uploady, na które
        // czekały blokady. Kolejność: konto, jego aplikacje, firma (równoległe usunięcie konta drugiego członka
        // tej samej firmy czeka - nie zostaną oba usunięte, zostawiając firmę z ofertami bez członków).
        if (await LockUserAsync(userId) == 0)
        {
            return AccountDeletion.Failed(AccountDeleteStatus.NotFound);
        }

        await _context.JobApplications
            .Where(a => a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, a => a.Status));

        await _context.Companies
            .Where(c => _context.Users.Any(u => u.Id == userId && u.CompanyId == c.Id))
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.Status, c => c.Status));

        var companyId = await _context.Users
            .Where(u => u.Id == userId)
            .Select(u => u.CompanyId)
            .FirstAsync();

        var deleteCompany = false;
        if (companyId.HasValue)
        {
            var otherMembers = await _context.Users.AnyAsync(u => u.CompanyId == companyId && u.Id != userId);
            if (!otherMembers)
            {
                if (await _context.JobOffers.AnyAsync(j => j.CompanyId == companyId))
                {
                    return AccountDeletion.Failed(AccountDeleteStatus.LastCompanyMember);
                }

                // firma bez ofert i bez innych członków (np. zgłoszenie czekające na weryfikację) nie zostaje osierocona
                deleteCompany = true;
            }
        }

        var fileKeys = await _context.ProfileDocuments
            .Where(d => d.UserId == userId)
            .Select(d => d.FileKey)
            .ToListAsync();
        fileKeys.AddRange(await _context.ApplicationAttachments
            .Where(a => a.JobApplication.UserId == userId)
            .Select(a => a.FileKey)
            .ToListAsync());

        // aplikacje (z historią i załącznikami), role, dokumenty, zapisane oferty i e-maile usuwane kaskadowo przez bazę;
        // wpisy historii innych aplikacji i decyzje o firmach tracą autora (SET NULL)
        await _context.Users.Where(u => u.Id == userId).ExecuteDeleteAsync();

        if (deleteCompany)
        {
            // zdjęcia i tło firmy (wiersz firmy jest już zablokowany - upload mediów czeka na tę transakcję);
            // zdjęcia usuwa kaskada w bazie, pliki - wywołujący po zatwierdzeniu
            var companyFileKeys = await CompanyRepository.CompanyFileKeysAsync(_context, companyId!.Value);

            var deletedCompanies = await _context.Companies
                .Where(c => c.Id == companyId && !c.Members.Any() && !c.JobOffers.Any())
                .ExecuteDeleteAsync();
            if (deletedCompanies > 0)
            {
                fileKeys.AddRange(companyFileKeys);
            }
        }

        await transaction.CommitAsync();
        return new AccountDeletion(AccountDeleteStatus.Deleted, fileKeys);
    }

    // UPDATE bez zmiany wartości tylko blokuje wiersz konta (wynik = liczba dopasowanych wierszy)
    private Task<int> LockUserAsync(string userId, CancellationToken cancellationToken = default) =>
        _context.Users
            .Where(u => u.Id == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(u => u.ConcurrencyStamp, u => u.ConcurrencyStamp), cancellationToken);
}
