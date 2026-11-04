using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobRepository
{
    // tylko oferty widoczne publicznie (Active i niewygasłe)
    Task<PagedList<JobOffer>> GetPublicJobsAsync(JobParams jobParams);
    // wszystkie oferty z opcjonalnym filtrem stanu (panel admina)
    Task<PagedList<JobOffer>> GetManagedJobsAsync(ManageJobParams jobParams);
    Task<JobOffer?> GetJobByIdAsync(int id);
    Task<JobOffer> AddJobAsync(JobOffer jobOffer);
    Task<JobOffer> UpdateJobAsync(JobOffer jobOffer);
    Task<bool> DeleteJobAsync(int id);
    Task<bool> JobExistsAsync(int id);
}
