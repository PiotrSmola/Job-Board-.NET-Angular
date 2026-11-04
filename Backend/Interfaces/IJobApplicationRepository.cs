using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobApplicationRepository
{
    // bez śledzenia; includeHistory dołącza historię statusów z autorami zmian
    Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false);
    // śledzona encja (bez nawigacji) do zmiany statusu - zapis przez UpdateApplicationAsync
    Task<JobApplication?> GetApplicationForUpdateAsync(int id);
    // śledzona aplikacja użytkownika na ofertę w dowolnym statusie (także wycofana)
    Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId);
    Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId);
    Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId);
    Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams);
    Task<JobApplication> AddApplicationAsync(JobApplication application);
    // zapisuje zmiany śledzonej aplikacji razem z nowymi wpisami historii (jeden SaveChanges = jedna transakcja)
    Task UpdateApplicationAsync(JobApplication application);
    Task<bool> DeleteApplicationAsync(int id);
}
