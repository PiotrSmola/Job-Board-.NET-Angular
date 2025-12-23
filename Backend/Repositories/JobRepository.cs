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
        var query = _context.JobOffers.AsQueryable();

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

        query = jobParams.OrderBy switch
        {
            "oldest" => query.OrderBy(j => j.CreatedAt),
            "salaryHigh" => query.OrderByDescending(j => j.SalaryMax ?? 0),
            "salaryLow" => query.OrderBy(j => j.SalaryMin ?? decimal.MaxValue),
            _ => query.OrderByDescending(j => j.CreatedAt)
        };

        return await PagedList<JobOffer>.CreateAsync(query, jobParams.PageNumber, jobParams.PageSize);
    }

    public async Task<JobOffer?> GetJobByIdAsync(int id)
    {
        return await _context.JobOffers
            .Include(j => j.Applications)
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

    public async Task<bool> SaveAllAsync()
    {
        return await _context.SaveChangesAsync() > 0;
    }
}
