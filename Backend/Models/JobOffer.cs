using System.ComponentModel.DataAnnotations;
using System.Linq.Expressions;

namespace Backend.Models;

public class JobOffer
{
    public int Id { get; set; }

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public int CompanyId { get; set; }

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

    // Navigation properties
    public Company Company { get; set; } = null!;
    public ICollection<JobApplication> Applications { get; set; } = new List<JobApplication>();

    // Aktywna, ale z datą ważności w przeszłości (stan wyliczany, nie zapisywany)
    public bool IsExpired(DateTime now) =>
        Status == JobOfferStatus.Active && ExpiresAt.HasValue && ExpiresAt.Value <= now;

    // Opublikowana: aktywna i niewygasła (sam stan oferty, bez statusu firmy)
    public bool IsPubliclyVisible(DateTime now) =>
        Status == JobOfferStatus.Active && (!ExpiresAt.HasValue || ExpiresAt.Value > now);

    // Widoczna publicznie (lista, szczegóły dla obcych, aplikowanie): opublikowana i firma zatwierdzona.
    // Wymaga załadowanej nawigacji Company.
    public bool IsListedPublicly(DateTime now) =>
        IsPubliclyVisible(now) && Company?.Status == CompanyStatus.Approved;

    // Ten sam warunek co IsPubliclyVisible - do zapytań SQL
    public static Expression<Func<JobOffer, bool>> PublishedAt(DateTime now) =>
        j => j.Status == JobOfferStatus.Active && (j.ExpiresAt == null || j.ExpiresAt > now);

    // Ten sam warunek co IsListedPublicly - do zapytań SQL
    public static Expression<Func<JobOffer, bool>> ListedPubliclyAt(DateTime now) =>
        j => j.Status == JobOfferStatus.Active && (j.ExpiresAt == null || j.ExpiresAt > now) &&
             j.Company.Status == CompanyStatus.Approved;
}
