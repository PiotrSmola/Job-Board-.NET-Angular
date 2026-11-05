using Backend.Models;

namespace Backend.Interfaces;

// Jedno miejsce decyzji "czy bieżący użytkownik może zarządzać firmą / ofertą / aplikacją".
// Admin zarządza wszystkim, pracodawca - tylko zasobami swojej firmy. Firma pracodawcy jest czytana z bazy
// (nie z JWT), więc zmiana przypisania działa od razu, bez ponownego logowania.
public interface ICurrentUserAccess
{
    string? UserId { get; }
    bool IsAdmin { get; }
    bool IsEmployer { get; }

    // firma pracodawcy; null dla innych ról albo konta pracodawcy bez firmy (wynik zapamiętany na czas żądania)
    Task<int?> GetEmployerCompanyIdAsync();

    // zakres list w panelu zarządzania; null = brak uprawnień (np. kandydat albo pracodawca bez firmy)
    Task<ManagementScope?> GetManagementScopeAsync();

    // admin albo pracodawca - członek firmy
    Task<bool> CanManageCompanyAsync(int companyId);

    // oferta należy do firmy, którą użytkownik zarządza
    Task<bool> CanManageJobAsync(JobOffer job);

    // aplikacja na ofertę firmy, którą użytkownik zarządza (firma oferty z nawigacji JobOffer albo z bazy)
    Task<bool> CanManageApplicationAsync(JobApplication application);

    // szczegóły oferty: zarządzający widzi każdą swoją ofertę, pozostali - poza szkicami i ofertami firm
    // niezatwierdzonych (wymaga załadowanej nawigacji Company)
    Task<bool> CanViewJobAsync(JobOffer job);
}

// Admin - wszystkie firmy (CompanyId = null), pracodawca - tylko jego firma
public sealed record ManagementScope(int? CompanyId)
{
    public static readonly ManagementScope AllCompanies = new((int?)null);
}
