using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobRepository
{
    // tylko oferty widoczne publicznie (Active, niewygasłe, firma zatwierdzona)
    Task<PagedList<JobOffer>> GetPublicJobsAsync(JobParams jobParams);
    // panel zarządzania: oferty z zakresu (admin - wszystkie, pracodawca - jego firmy), opcjonalny filtr stanu
    Task<PagedList<JobOffer>> GetManagedJobsAsync(ManageJobParams jobParams, ManagementScope scope);
    // bez śledzenia, z nawigacją Company
    Task<JobOffer?> GetJobByIdAsync(int id);
    Task<JobOffer> AddJobAsync(JobOffer jobOffer);
    Task<JobOffer> UpdateJobAsync(JobOffer jobOffer);
    Task<bool> DeleteJobAsync(int id);
    // usuwa ofertę tylko, gdy nie ma żadnych aplikacji (jedno zapytanie DELETE ... NOT EXISTS - bez wyścigu z aplikowaniem)
    Task<bool> DeleteJobWithoutApplicationsAsync(int id);
    Task<bool> JobExistsAsync(int id);
}
