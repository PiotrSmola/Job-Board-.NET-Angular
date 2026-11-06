using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class SavedJobRepository : ISavedJobRepository
{
    private readonly ApplicationDbContext _context;

    public SavedJobRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> SaveAsync(string userId, int jobOfferId, DateTime savedAt)
    {
        if (await _context.SavedJobs.AnyAsync(s => s.UserId == userId && s.JobOfferId == jobOfferId))
        {
            return true;
        }

        var savedJob = new SavedJob { UserId = userId, JobOfferId = jobOfferId, SavedAt = savedAt };
        _context.SavedJobs.Add(savedJob);
        try
        {
            await _context.SaveChangesAsync();
            return true;
        }
        catch (Exception ex) when (ex.IsDuplicateKey() || ex.IsMissingReference())
        {
            // równoległe żądanie zapisało już tę ofertę (klucz główny) albo oferta zniknęła (klucz obcy)
            _context.Entry(savedJob).State = EntityState.Detached;
            return ex.IsDuplicateKey();
        }
    }

    public async Task RemoveAsync(string userId, int jobOfferId)
    {
        await _context.SavedJobs
            .Where(s => s.UserId == userId && s.JobOfferId == jobOfferId)
            .ExecuteDeleteAsync();
    }

    public async Task<PagedList<SavedJob>> GetSavedJobsAsync(string userId, SavedJobParams savedJobParams)
    {
        // kategoria i umiejętności potrzebne do JobOfferDto (jak na publicznej liście)
        var query = Visible(userId)
            .Include(s => s.JobOffer).ThenInclude(j => j.Company)
            .Include(s => s.JobOffer).ThenInclude(j => j.Category)
            .Include(s => s.JobOffer).ThenInclude(j => j.Skills)
            .AsNoTracking()
            .OrderByDescending(s => s.SavedAt)
            .ThenByDescending(s => s.JobOfferId);

        return await PagedList<SavedJob>.CreateAsync(query, savedJobParams.PageNumber, savedJobParams.PageSize);
    }

    public async Task<List<int>> GetSavedJobIdsAsync(string userId)
    {
        return await Visible(userId)
            .OrderByDescending(s => s.SavedAt)
            .Select(s => s.JobOfferId)
            .ToListAsync();
    }

    // oferty widoczne dla kandydata (jak ICurrentUserAccess.CanViewJobAsync): bez szkiców i ofert firm niezatwierdzonych
    private IQueryable<SavedJob> Visible(string userId) =>
        _context.SavedJobs.Where(s => s.UserId == userId
                                      && s.JobOffer.Status != JobOfferStatus.Draft
                                      && s.JobOffer.Company.Status == CompanyStatus.Approved);
}
