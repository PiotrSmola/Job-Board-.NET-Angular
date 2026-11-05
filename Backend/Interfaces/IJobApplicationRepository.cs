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
    // panel zarządzania: aplikacje do ofert z zakresu (admin - wszystkie, pracodawca - jego firmy)
    Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope);
    Task<JobApplication> AddApplicationAsync(JobApplication application);
    // w jednej transakcji blokuje wiersz śledzonej aplikacji, wykonuje change(czas zapisu) i zapisuje aplikację
    // z nowymi wpisami historii; jeśli aplikację od wczytania zmieniło inne żądanie (inny token StatusChangedAt),
    // change nie jest wykonywane i kończy się to DbUpdateConcurrencyException
    Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change);
    Task<bool> DeleteApplicationAsync(int id);
}
