using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Repositories;

public class JobRepository : IJobRepository
{
    private readonly ApplicationDbContext _context;

    public JobRepository(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<PagedList<JobOffer>> GetJobsAsync(JobParams jobParams)
    {
        var query = _context.JobOffers.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(jobParams.SearchTerm))
        {
            query = query.Where(j =>
                j.Title.ToLower().Contains(jobParams.SearchTerm.ToLower()) ||
                j.Company.ToLower().Contains(jobParams.SearchTerm.ToLower()) ||
                j.Description.ToLower().Contains(jobParams.SearchTerm.ToLower())
            );
        }

        if (jobParams.WorkType.HasValue)
        {
            query = query.Where(j => j.WorkType == jobParams.WorkType.Value);
        }

        if (!string.IsNullOrWhiteSpace(jobParams.Location))
        {
            query = query.Where(j => j.Location.ToLower().Contains(jobParams.Location.ToLower()));
        }

        if (jobParams.MinSalary.HasValue && jobParams.MaxSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin >= jobParams.MinSalary.Value) &&
                (j.SalaryMax.HasValue && j.SalaryMax <= jobParams.MaxSalary.Value)
            );
        }
        else if (jobParams.MinSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin >= jobParams.MinSalary.Value) ||
                (j.SalaryMax.HasValue && j.SalaryMax >= jobParams.MinSalary.Value)
            );
        }
        else if (jobParams.MaxSalary.HasValue)
        {
            query = query.Where(j =>
                (j.SalaryMin.HasValue && j.SalaryMin <= jobParams.MaxSalary.Value) ||
                (j.SalaryMax.HasValue && j.SalaryMax <= jobParams.MaxSalary.Value)
            );
        }

        // Id jako drugi klucz sortowania zapewnia stabilną kolejność między stronami
        query = jobParams.OrderBy switch
        {
            "oldest" => query.OrderBy(j => j.CreatedAt).ThenBy(j => j.Id),
            // wynagrodzenie: oba kierunki po tym samym kluczu (górna granica widełek, a gdy jej brak - dolna),
            // więc salaryLow jest dokładnym odwróceniem salaryHigh; oferty bez widełek zawsze na końcu
            "salaryHigh" => query
                .OrderBy(j => (j.SalaryMax ?? j.SalaryMin) == null)
                .ThenByDescending(j => j.SalaryMax ?? j.SalaryMin)
                .ThenByDescending(j => j.Id),
            "salaryLow" => query
                .OrderBy(j => (j.SalaryMax ?? j.SalaryMin) == null)
                .ThenBy(j => j.SalaryMax ?? j.SalaryMin)
                .ThenBy(j => j.Id),
            "titleAsc" => query.OrderBy(j => j.Title).ThenBy(j => j.Id),
            "titleDesc" => query.OrderByDescending(j => j.Title).ThenByDescending(j => j.Id),
            "companyAsc" => query.OrderBy(j => j.Company).ThenBy(j => j.Id),
            "companyDesc" => query.OrderByDescending(j => j.Company).ThenByDescending(j => j.Id),
            "locationAsc" => query.OrderBy(j => j.Location).ThenBy(j => j.Id),
            "locationDesc" => query.OrderByDescending(j => j.Location).ThenByDescending(j => j.Id),
            _ => query.OrderByDescending(j => j.CreatedAt).ThenByDescending(j => j.Id)
        };

        return await PagedList<JobOffer>.CreateAsync(query, jobParams.PageNumber, jobParams.PageSize);
    }

    public async Task<JobOffer?> GetJobByIdAsync(int id)
    {
        // bez śledzenia - przy aktualizacji UpdateJobAsync dołącza encję do kontekstu
        return await _context.JobOffers
            .AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == id);
    }

    public async Task<JobOffer> AddJobAsync(JobOffer jobOffer)
    {
        _context.JobOffers.Add(jobOffer);
        await _context.SaveChangesAsync();
        return jobOffer;
    }

    public async Task<JobOffer> UpdateJobAsync(JobOffer jobOffer)
    {
        _context.Entry(jobOffer).State = EntityState.Modified;
        await _context.SaveChangesAsync();
        return jobOffer;
    }

    public async Task<bool> DeleteJobAsync(int id)
    {
        var job = await _context.JobOffers.FindAsync(id);
        if (job == null) return false;

        _context.JobOffers.Remove(job);
        return await SaveAllAsync();
    }

    public async Task<bool> JobExistsAsync(int id)
    {
        return await _context.JobOffers.AnyAsync(j => j.Id == id);
    }

    private async Task<bool> SaveAllAsync()
    {
        return await _context.SaveChangesAsync() > 0;
    }
}
