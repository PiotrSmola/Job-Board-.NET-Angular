using Backend.Models;

namespace Backend.DTOs;

// Typ odpowiedzi - walidacja wejścia jest w CreateJobOfferDto i UpdateJobOfferDto
public class JobOfferDto
{
    public int Id { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Company { get; set; } = string.Empty;

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
}
