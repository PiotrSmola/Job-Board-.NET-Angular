using Backend.Models;

namespace Backend.DTOs;

// Typ odpowiedzi - walidacja wejścia jest w CreateJobOfferDto i UpdateJobOfferDto
public class JobOfferDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    // nazwa firmy (zgodność wsteczna z polem tekstowym sprzed wprowadzenia firm)
    public string Company { get; set; } = string.Empty;

    public int CompanyId { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    public string Location { get; set; } = string.Empty;

    public WorkType WorkType { get; set; }

    public DateTime CreatedAt { get; set; }

    public JobOfferStatus Status { get; set; }

    public DateTime? ExpiresAt { get; set; }

    // Active z ExpiresAt w przeszłości (wyliczane w chwili odpowiedzi)
    public bool IsExpired { get; set; }

    // Taksonomia: kategoria (null = bez kategorii), poziom, maska typów umów, waluta i okres widełek, umiejętności
    public int? CategoryId { get; set; }

    public string? CategoryName { get; set; }

    public string? CategorySlug { get; set; }

    public SeniorityLevel? Seniority { get; set; }

    public ContractTypes ContractTypes { get; set; }

    public Currency SalaryCurrency { get; set; }

    public SalaryPeriod SalaryPeriod { get; set; }

    // posortowane alfabetycznie
    public List<string> Skills { get; set; } = new();

    // tło nagłówka oferty z ustawień firmy; null - firma bez obrazu tła albo niezatwierdzona
    public OfferBackgroundDto? CompanyBackground { get; set; }
}
