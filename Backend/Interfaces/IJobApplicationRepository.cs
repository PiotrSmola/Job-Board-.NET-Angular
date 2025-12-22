using Backend.Models;

namespace Backend.Interfaces;

public interface IJobApplicationRepository
{
    Task<JobApplication?> GetApplicationByIdAsync(int id);
    Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId);
    Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId);
    Task<bool> HasUserAppliedAsync(int jobId, string userId);
    Task<JobApplication> AddApplicationAsync(JobApplication application);
    Task<bool> DeleteApplicationAsync(int id);
    Task<bool> SaveAllAsync();
}
