using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobApplicationRepository
{
    // bez śledzenia; includeHistory dołącza historię statusów z autorami zmian
    Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default);
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
    // Zapisuje CV (cv = null usuwa je) jednym warunkowym UPDATE: tylko aplikacja kandydata userId w statusie
    // pozwalającym na zmianę CV, której bieżący klucz pliku to expectedFileKey (token - równoległa zmiana CV,
    // usunięcie aplikacji albo decyzja końcowa). false - warunek niespełniony, nic nie zapisano.
    Task<bool> UpdateCvAsync(int id, string userId, string? expectedFileKey, CvFile? cv, CancellationToken cancellationToken = default);
    // usuwa aplikację (z historią); CvFileKey - klucz pliku CV usuniętej aplikacji do usunięcia z magazynu
    Task<(bool Deleted, string? CvFileKey)> DeleteApplicationAsync(int id);
}
