using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface IJobRepository
{
    // tylko oferty widoczne publicznie (Active, niewygasłe, firma zatwierdzona)
    Task<PagedList<JobOffer>> GetPublicJobsAsync(JobParams jobParams);
    // liczniki faset publicznej listy; licznik fasety pomija jej własny filtr (reszta filtrów jak w GetPublicJobsAsync)
    Task<JobFacets> GetPublicFacetsAsync(JobParams jobParams, CancellationToken cancellationToken = default);
    // panel zarządzania: oferty z zakresu (admin - wszystkie, pracodawca - jego firmy), opcjonalny filtr stanu
    Task<PagedList<JobOffer>> GetManagedJobsAsync(ManageJobParams jobParams, ManagementScope scope);
    // bez śledzenia, z nawigacjami Company, Category i Skills
    Task<JobOffer?> GetJobByIdAsync(int id);
    // skillNames - nazwy po normalizacji, bez duplikatów (JobOfferFormDto.GetSkillNames); nowe umiejętności są tworzone
    Task<JobOffer> AddJobAsync(JobOffer jobOffer, IReadOnlyList<string> skillNames);
    // skillNames zastępują dotychczasowe umiejętności oferty
    Task<JobOffer> UpdateJobAsync(JobOffer jobOffer, IReadOnlyList<string>? skillNames = null);
    // admin: usuwa ofertę z aplikacjami; FileKeys - klucze plików załączników usuniętych aplikacji do usunięcia z magazynu
    Task<(bool Deleted, IReadOnlyList<string> FileKeys)> DeleteJobAsync(int id);
    // usuwa ofertę tylko, gdy nie ma żadnych aplikacji (jedno zapytanie DELETE ... NOT EXISTS - bez wyścigu z aplikowaniem)
    Task<bool> DeleteJobWithoutApplicationsAsync(int id);
    Task<bool> JobExistsAsync(int id);
}
