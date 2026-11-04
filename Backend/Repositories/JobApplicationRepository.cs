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

    public async Task<JobApplication?> GetApplicationByIdAsync(int id)
    {
        return await _context.JobApplications
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .FirstOrDefaultAsync(a => a.Id == id);
    }

    public async Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId)
    {
        return await _context.JobApplications
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .Where(a => a.UserId == userId)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
    }

    public async Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId)
    {
        return await _context.JobApplications
            .Include(a => a.User)
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

    public async Task<bool> HasUserAppliedAsync(int jobId, string userId)
    {
        return await _context.JobApplications
            .AnyAsync(a => a.JobOfferId == jobId && a.UserId == userId);
    }

    public async Task<JobApplication> AddApplicationAsync(JobApplication application)
    {
        _context.JobApplications.Add(application);
        await _context.SaveChangesAsync();
        return application;
    }

    public async Task<bool> DeleteApplicationAsync(int id)
    {
        var application = await _context.JobApplications.FindAsync(id);
        if (application == null) return false;

        _context.JobApplications.Remove(application);
        return await SaveAllAsync();
    }

    public async Task<bool> SaveAllAsync()
    {
        return await _context.SaveChangesAsync() > 0;
    }
}
