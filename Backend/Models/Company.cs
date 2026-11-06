using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class Company
{
    public int Id { get; set; }

    // unikalna (bez rozróżniania wielkości liter - kolacja bazy); zapisywana po normalizacji (CompanyNames.Normalize)
    [Required]
    [MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string? Description { get; set; }

    [MaxLength(300)]
    public string? Website { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    public CompanyStatus Status { get; set; } = CompanyStatus.Pending;

    [MaxLength(1000)]
    public string? RejectionReason { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // data i autor ostatniej decyzji admina (zatwierdzenie / odrzucenie)
    public DateTime? ReviewedAt { get; set; }

    // null po usunięciu konta admina
    public string? ReviewedByUserId { get; set; }

    // Navigation properties
    public ApplicationUser? ReviewedByUser { get; set; }
    public ICollection<ApplicationUser> Members { get; set; } = new List<ApplicationUser>();
    public ICollection<JobOffer> JobOffers { get; set; } = new List<JobOffer>();

    public void Approve(string? reviewerId, DateTime now)
    {
        Status = CompanyStatus.Approved;
        RejectionReason = null;
        ReviewedAt = now;
        ReviewedByUserId = reviewerId;
    }

    public void Reject(string reason, string? reviewerId, DateTime now)
    {
        Status = CompanyStatus.Rejected;
        RejectionReason = reason;
        ReviewedAt = now;
        ReviewedByUserId = reviewerId;
    }

    // Ponowne zgłoszenie do weryfikacji: odrzucona firma po edycji profilu albo zatwierdzona po zmianie nazwy / www
    public void Resubmit()
    {
        Status = CompanyStatus.Pending;
        RejectionReason = null;
        ReviewedAt = null;
        ReviewedByUserId = null;
    }
}
