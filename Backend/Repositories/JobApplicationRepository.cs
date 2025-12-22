using Backend.Data;
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

    public async Task<IEnumerable<JobApplication>> GetAllApplicationsAsync()
    {
        return await _context.JobApplications
            .Include(a => a.JobOffer)
            .Include(a => a.User)
            .OrderByDescending(a => a.AppliedAt)
            .ToListAsync();
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
