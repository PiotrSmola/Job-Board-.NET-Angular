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
            .AsNoTracking();

        if (includeHistory)
        {
            query = query.Include(a => a.StatusHistory).ThenInclude(h => h.ChangedByUser);
        }

        return await query.FirstOrDefaultAsync(a => a.Id == id, cancellationToken);
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
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId)
    {
        return await _context.JobApplications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
            .AsNoTracking()
            .Where(a => a.JobOfferId == jobId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
    }

    public async Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope)
    {
        var query = _context.JobApplications
            .Include(a => a.JobOffer).ThenInclude(j => j.Company)
            .Include(a => a.User)
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

    public async Task<bool> UpdateCvAsync(int id, string userId, string? expectedFileKey, CvFile? cv, CancellationToken cancellationToken = default)
    {
        var statuses = JobApplication.CvEditableStatuses.ToArray();
        var key = cv?.Key;
        var fileName = cv?.FileName;
        var contentType = cv?.ContentType;
        var size = cv?.Size;
        var uploadedAt = cv?.UploadedAt;

        // pojedyncza instrukcja jest atomowa - nie potrzeba osobnej transakcji ani blokady; StatusChangedAt
        // (token zmian statusu) nie jest zmieniany, więc CV nie koliduje z równoległą zmianą statusu na dozwolony
        var matched = await _context.JobApplications
            .Where(a => a.Id == id && a.UserId == userId && a.CvFileKey == expectedFileKey && statuses.Contains(a.Status))
            .ExecuteUpdateAsync(s => s
                .SetProperty(a => a.CvFileKey, key)
                .SetProperty(a => a.CvFileName, fileName)
                .SetProperty(a => a.CvContentType, contentType)
                .SetProperty(a => a.CvSize, size)
                .SetProperty(a => a.CvUploadedAt, uploadedAt), cancellationToken);

        return matched > 0;
    }

    public async Task<(bool Deleted, string? CvFileKey)> DeleteApplicationAsync(int id)
    {
        // Blokada wiersza przed odczytem klucza CV: równoległa zamiana CV (UpdateCvAsync) czeka do końca transakcji
        // i trafia już na usuniętą aplikację, więc odczytany klucz jest ostatnim zapisanym (plik nie zostanie osierocony)
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var matched = await _context.JobApplications
            .Where(a => a.Id == id)
            .ExecuteUpdateAsync(s => s.SetProperty(a => a.Status, a => a.Status));
        if (matched == 0)
        {
            return (false, null);
        }

        var cvFileKey = await _context.JobApplications
            .Where(a => a.Id == id)
            .Select(a => a.CvFileKey)
            .FirstOrDefaultAsync();

        // historia statusów usuwana kaskadowo przez bazę
        await _context.JobApplications.Where(a => a.Id == id).ExecuteDeleteAsync();

        await transaction.CommitAsync();
        return (true, cvFileKey);
    }
}
