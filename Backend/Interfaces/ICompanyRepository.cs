using Backend.Helpers;
using Backend.Models;

namespace Backend.Interfaces;

public interface ICompanyRepository
{
    // tylko firmy zatwierdzone (publiczna lista)
    Task<PagedList<CompanySummary>> GetPublicCompaniesAsync(CompanyParams companyParams);
    // wszystkie firmy z opcjonalnym filtrem statusu (panel admina)
    Task<PagedList<CompanySummary>> GetCompaniesAsync(AdminCompanyParams companyParams);
    Task<CompanySummary?> GetSummaryAsync(int id);
    // śledzona encja do edycji - zapis przez UpdateCompanyAsync
    Task<Company?> GetCompanyForUpdateAsync(int id);
    Task<CompanyStatus?> GetStatusAsync(int id);
    // nazwa zajęta przez inną firmę (bez rozróżniania wielkości liter); name - po CompanyNames.Normalize
    Task<bool> IsNameTakenAsync(string name, int? exceptCompanyId = null);
    Task<bool> HasJobsOrMembersAsync(int id);
    Task AddCompanyAsync(Company company);
    Task UpdateCompanyAsync(Company company);
    Task DeleteCompanyAsync(Company company);
}
