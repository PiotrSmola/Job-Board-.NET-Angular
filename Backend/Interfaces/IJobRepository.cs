using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobRepository
{
    Task<PagedList<JobOffer>> GetJobsAsync(JobParams jobParams);
    Task<JobOffer?> GetJobByIdAsync(int id);
    Task<JobOffer> AddJobAsync(JobOffer jobOffer);
    Task<JobOffer> UpdateJobAsync(JobOffer jobOffer);
    Task<bool> DeleteJobAsync(int id);
    Task<bool> JobExistsAsync(int id);
    Task<bool> SaveAllAsync();
}
