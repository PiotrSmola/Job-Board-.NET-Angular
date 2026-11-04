using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class JobOffer
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required]
    [MaxLength(5000)]
    public string Description { get; set; } = string.Empty;

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    [Required]
    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;

    public WorkType WorkType { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public JobOfferStatus Status { get; set; } = JobOfferStatus.Active;

    // UTC; null = oferta bezterminowa
    public DateTime? ExpiresAt { get; set; }

    // Navigation property
    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();

    // Aktywna, ale z datą ważności w przeszłości (stan wyliczany, nie zapisywany)
    public bool IsExpired(DateTime now) =>
        Status == JobOfferStatus.Active && ExpiresAt.HasValue && ExpiresAt.Value <= now;

    // Widoczna na publicznej liście i przyjmuje aplikacje
    public bool IsPubliclyVisible(DateTime now) =>
        Status == JobOfferStatus.Active && (!ExpiresAt.HasValue || ExpiresAt.Value > now);
}
