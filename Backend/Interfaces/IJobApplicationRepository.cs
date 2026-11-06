using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobApplicationRepository
{
    // bez śledzenia, z załącznikami; includeHistory dołącza historię statusów z autorami zmian
    Task<JobApplication?> GetApplicationByIdAsync(int id, bool includeHistory = false, CancellationToken cancellationToken = default);
    // lekkie zapytanie (właściciel i status) - kontrola uprawnień przed odczytem treści żądania uploadu
    Task<ApplicationOwnership?> GetOwnershipAsync(int id, CancellationToken cancellationToken = default);
    // śledzona encja (bez nawigacji) do zmiany statusu - zapis przez UpdateApplicationAsync
    Task<JobApplication?> GetApplicationForUpdateAsync(int id);
    // śledzona aplikacja użytkownika na ofertę w dowolnym statusie (także wycofana)
    Task<JobApplication?> GetUserApplicationForJobAsync(int jobId, string userId);
    // listy z załącznikami (jedno zapytanie, bez N+1)
    Task<IEnumerable<JobApplication>> GetApplicationsByUserIdAsync(string userId);
    Task<IEnumerable<JobApplication>> GetApplicationsByJobIdAsync(int jobId);
    // panel zarządzania: aplikacje do ofert z zakresu (admin - wszystkie, pracodawca - jego firmy)
    Task<PagedList<JobApplication>> GetApplicationsAsync(ApplicationParams applicationParams, ManagementScope scope);
    Task<JobApplication> AddApplicationAsync(JobApplication application);
    // w jednej transakcji blokuje wiersz śledzonej aplikacji, wykonuje change(czas zapisu) i zapisuje aplikację
    // z nowymi wpisami historii; jeśli aplikację od wczytania zmieniło inne żądanie (inny token StatusChangedAt),
    // change nie jest wykonywane i kończy się to DbUpdateConcurrencyException
    Task UpdateApplicationAsync(JobApplication application, Action<DateTime> change);
    // Zapisuje rekordy załączników (pliki są już w magazynie) w transakcji z blokadą wiersza aplikacji: tylko
    // aplikacja kandydata userId w statusie pozwalającym na dodawanie i tylko gdy po dodaniu liczba i suma rozmiarów
    // załączników mieszczą się w limitach. Inny wynik niż Added - nic nie zapisano.
    Task<AttachmentsAddResult> AddAttachmentsAsync(int applicationId, string userId, IReadOnlyList<ApplicationAttachment> attachments,
        CancellationToken cancellationToken = default);
    // usuwa załącznik aplikacji kandydata userId (w każdym statusie); FileKey - klucz pliku do usunięcia z magazynu
    Task<(AttachmentDeleteResult Result, string? FileKey)> DeleteAttachmentAsync(int applicationId, int attachmentId, string userId,
        CancellationToken cancellationToken = default);
    // usuwa aplikację (z historią i załącznikami); FileKeys - klucze plików załączników do usunięcia z magazynu
    Task<(bool Deleted, IReadOnlyList<string> FileKeys)> DeleteApplicationAsync(int id);
}

public sealed record ApplicationOwnership(string UserId, ApplicationStatus Status);

public enum AttachmentsAddResult
{
    Added,
    ApplicationNotFound,
    StatusNotAllowed,
    TooMany,
    TotalTooLarge
}

public enum AttachmentDeleteResult
{
    Deleted,
    ApplicationNotFound,
    AttachmentNotFound
}
