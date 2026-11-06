using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class JobApplicationRepository : IJobApplicationRepository
{
    private readonly ApplicationDbContext _context;

    public JobApplicationRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default)
    {
        var query = _context.JobApplications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .Include(a => a.Attachments)
            .AsNoTracking();

        if (includeHistory)
        {
            // dwie kolekcje (załączniki i historia) - osobne zapytania zamiast iloczynu kartezjańskiego w jednym
            query = query.Include(a => a.StatusHistory).ThenInclude(h => h.ChangedByUser).AsSplitQuery();
        }

        return await query.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
    }

    public async Task<ApplicationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default)
    {
        return await _context.JobApplications
            .Where(a => a.Id == id)
            .Select(a => new ApplicationOwnership(a.UserId, a.Status))
            .FirstOrDefaultAsync(cancellationToken);
    }

    public async Task<JobApplication?> GetApplicationForUpdateAsync(int id)
    {
        return await _context.JobApplications.FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId)
    {
        return await _context.JobApplications
            .FirstOrDefaultAsync(a => a.JobOfferId == jobId && a.UserId == userId);
    }

    public async Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId)
    {
        return await _context.JobApplications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .Include(a => a.Attachments)
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
    }

    public async Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope)
    {
        var query = _context.JobApplications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .Include(a => a.Attachments)
            .AsNoTracking();

        // pracodawca widzi wyłącznie aplikacje do ofert swojej firmy
        if (scope.CompanyId.HasValue)
        {
            query = query.Where(a => a.JobOffer.CompanyId == scope.CompanyId.Value);
        }

        if (applicationParams.JobOfferId.HasValue)
        {
            query = query.Where(a => a.JobOfferId == applicationParams.JobOfferId.Value);
        }

        if (applicationParams.Status.HasValue)
        {
            query = query.Where(a => a.Status == applicationParams.Status.Value);
        }

        if (!string.IsNullOrWhiteSpace(applicationParams.SearchTerm))
        {
            var term = applicationParams.SearchTerm.Trim().ToLower();
            query = query.Where(a =>
                (a.User.Email != null && a.User.Email.ToLower().Contains(term)) ||
                (a.User.FirstName != null && a.User.FirstName.ToLower().Contains(term)) ||
                (a.User.LastName != null && a.User.LastName.ToLower().Contains(term)) ||
                (a.User.FirstName + " " + a.User.LastName).ToLower().Contains(term) ||
                a.JobOffer.Title.ToLower().Contains(term) ||
                a.JobOffer.Company.Name.ToLower().Contains(term)
            );
        }

        query = applicationParams.OrderBy switch
        {
            "oldest" => query.OrderBy(a => a.AppliedAt).ThenBy(a => a.Id),
            _ => query.OrderByDescending(a => a.AppliedAt).ThenByDescending(a => a.Id)
        };

        return await PagedList<JobApplication>.CreateAsync(query, applicationParams.PageNumber, applicationParams.PageSize);
    }

    public async Task<JobApplication> AddApplicationAsync(JobApplication application)
    {
        // aplikacja i jej pierwszy wpis historii w jednym SaveChanges
        _context.JobApplications.Add(application);
        await _context.SaveChangesAsync();
        return application;
    }

    public async Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change)
    {
        var entry = _context.Entry(application);
        if (entry.State == EntityState.Detached)
        {
            throw new InvalidOperationException("Application must be loaded with GetApplicationForUpdateAsync or GetUserApplicationForJobAsync");
        }

        var loadedToken = entry.Property(a => a.StatusChangedAt).OriginalValue;

        // Kolejność w transakcji ma znaczenie. Najpierw blokada X wiersza aplikacji z kontrolą tokenu, dopiero potem
        // zapis aplikacji i INSERT wpisów historii. W zwykłym SaveChanges INSERT historii zakłada przez klucz obcy
        // blokadę S na aplikacji przed jej UPDATE, a dwie równoległe transakcje podnoszące S -> X kończą się
        // deadlockiem MySQL. Blokada obejmuje też notatki (ten sam status) - notatka musi trafić na aktualny stan.
        await using var transaction = await _context.Database.BeginTransactionAsync();

        // UPDATE bez zmiany wartości tylko blokuje wiersz; Pomelo wyłącza UseAffectedRows, więc wynik to liczba
        // dopasowanych wierszy (1 także wtedy, gdy nic się nie zmieniło)
        var matched = await _context.JobApplications
            .Where(a => a.Id == application.Id && a.StatusChangedAt == loadedToken)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, a => a.Status));

        if (matched == 0)
        {
            // StatusChangedAt jest tokenem współbieżności - aplikację zmieniło w międzyczasie inne żądanie
            throw new DbUpdateConcurrencyException("Application was changed by another request");
        }

        // zmiana z czasem ustalonym dopiero po uzyskaniu blokady - kolejność dat w historii odpowiada kolejności
        // zapisów (np. notatka zapisana przed równoległym wycofaniem ma wcześniejszą datę niż wycofanie)
        change(DateTime.UtcNow);

        // wiersz jest już zablokowany przez tę transakcję - UPDATE (z ponowną kontrolą tokenu) i INSERT historii
        await _context.SaveChangesAsync();

        await transaction.CommitAsync();
    }

    public async Task<AttachmentsAddResult> AddAttachmentsAsync(int applicationId, string userId,
        IReadOnlyList<ApplicationAttachment> attachments, CancellationToken cancellationToken = default)
    {
        // Limity liczby i łącznego rozmiaru sprawdzane pod blokadą X wiersza aplikacji: równoległe uploady do tej samej
        // aplikacji czekają tu na siebie, a każdy liczy już zatwierdzone załączniki poprzednika (pierwszy odczyt
        // spójny w transakcji następuje po uzyskaniu blokady). Tę samą blokadę biorą usunięcie aplikacji i oferty,
        // więc odczytane tam klucze plików obejmują załączniki zapisane tutaj.
        await using var transaction = await _context.Database.BeginTransactionAsync(cancellationToken);

        // UPDATE bez zmiany wartości tylko blokuje wiersz (wynik = liczba dopasowanych wierszy, jak w UpdateApplicationAsync)
        var matched = await _context.JobApplications
            .Where(a => a.Id == applicationId && a.UserId == userId)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, a => a.Status), cancellationToken);
        if (matched == 0)
        {
            return AttachmentsAddResult.ApplicationNotFound;
        }

        var status = await _context.JobApplications
            .Where(a => a.Id == applicationId)
            .Select(a => a.Status)
            .FirstAsync(cancellationToken);
        if (!JobApplication.AttachmentUploadStatuses.Contains(status))
        {
            return AttachmentsAddResult.StatusNotAllowed;
        }

        var existing = await _context.ApplicationAttachments
            .Where(a => a.JobApplicationId == applicationId)
            .GroupBy(a => a.JobApplicationId)
            .Select(g => new { Count = g.Count(), Size = g.Sum(a => a.Size) })
            .FirstOrDefaultAsync(cancellationToken);

        if ((existing?.Count ?? 0) + attachments.Count > AttachmentFiles.MaxAttachments)
        {
            return AttachmentsAddResult.TooMany;
        }

        if ((existing?.Size ?? 0) + attachments.Sum(a => a.Size) > AttachmentFiles.MaxTotalSize)
        {
            return AttachmentsAddResult.TotalTooLarge;
        }

        foreach (var attachment in attachments)
        {
            attachment.JobApplicationId = applicationId;
        }

        _context.ApplicationAttachments.AddRange(attachments);
        await _context.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return AttachmentsAddResult.Added;
    }

    public async Task<IReadOnlyList<string>> GetExistingFileKeysAsync(IReadOnlyCollection<string> fileKeys,
        CancellationToken cancellationToken = default)
    {
        if (fileKeys.Count == 0)
        {
            return [];
        }

        return await _context.ApplicationAttachments
            .AsNoTracking()
            .Where(a => fileKeys.Contains(a.FileKey))
            .Select(a => a.FileKey)
            .ToListAsync(cancellationToken);
    }

    public async Task<(AttachmentDeleteResult Result, string? FileKey)> DeleteAttachmentAsync(int applicationId, int attachmentId,
        string userId, CancellationToken cancellationToken = default)
    {
        if (!await _context.JobApplications.AnyAsync(a => a.Id == applicationId && a.UserId == userId, cancellationToken))
        {
            return (AttachmentDeleteResult.ApplicationNotFound, null);
        }

        var fileKey = await _context.ApplicationAttachments
            .Where(a => a.Id == attachmentId && a.JobApplicationId == applicationId)
            .Select(a => a.FileKey)
            .FirstOrDefaultAsync(cancellationToken);
        if (fileKey == null)
        {
            return (AttachmentDeleteResult.AttachmentNotFound, null);
        }

        // klucz załącznika się nie zmienia, więc odczyt przed DELETE jest bezpieczny; z równoległych usunięć tego
        // samego załącznika tylko jedno usuwa wiersz (i plik), pozostałe dostają 404
        var deleted = await _context.ApplicationAttachments
            .Where(a => a.Id == attachmentId && a.JobApplicationId == applicationId)
            .ExecuteDeleteAsync(cancellationToken);

        return deleted == 0 ? (AttachmentDeleteResult.AttachmentNotFound, null) : (AttachmentDeleteResult.Deleted, fileKey);
    }

    public async Task<(bool Deleted, IReadOnlyList<string> FileKeys)> DeleteApplicationAsync(int id)
    {
        // Blokada wiersza przed odczytem kluczy plików: równoległy upload (AddAttachmentsAsync) czeka do końca
        // transakcji i trafia już na usuniętą aplikację, więc odczytane klucze obejmują wszystkie zapisane załączniki
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var matched = await _context.JobApplications
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, a => a.Status));
        if (matched == 0)
        {
            return (false, []);
        }

        var fileKeys = await _context.ApplicationAttachments
            .Where(a => a.JobApplicationId == id)
            .Select(a => a.FileKey)
            .ToListAsync();

        // historia statusów i załączniki usuwane kaskadowo przez bazę
        await _context.JobApplications.Where(a => a.Id == id).ExecuteDeleteAsync();

        await transaction.CommitAsync();
        return (true, fileKeys);
    }
}
