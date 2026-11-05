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

    public async Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false)
    {
        var query = _context.JobApplications
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .AsNoTracking();

        if (includeHistory)
        {
            query = query.Include(a => a.StatusHistory).ThenInclude(h => h.ChangedByUser);
        }

        return await query.FirstOrDefaultAsync(a => a.Id == id);
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
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .AsNoTracking()
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId)
    {
        return await _context.JobApplications
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .AsNoTracking()
            .Where(a => a.JobOfferId == jobId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
    }

    public async Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams)
    {
        var query = _context.JobApplications
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .AsNoTracking();

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
                a.JobOffer.Company.ToLower().Contains(term)
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

    public async Task<bool> DeleteApplicationAsync(int id)
    {
        var application = await _context.JobApplications.FindAsync(id);
        if (application == null) return false;

        _context.JobApplications.Remove(application);
        return await SaveAllAsync();
    }

    private async Task<bool> SaveAllAsync()
    {
        return await _context.SaveChangesAsync() > 0;
    }
}
