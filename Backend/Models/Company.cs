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

    // Tło nagłówka ofert firmy: klucz pliku w magazynie i typ ustalony przez serwer (null - brak tła); przyciemnienie
    // w procentach (CompanyMedia.MinBackgroundDim-MaxBackgroundDim) i punkt zaczepienia obrazu. Wartości domyślne
    // kolumn (75, Center) są też w migracji - istniejące firmy dostają je bez danych w kodzie
    [MaxLength(300)]
    public string? OfferBackgroundKey { get; set; }

    [MaxLength(100)]
    public string? OfferBackgroundContentType { get; set; }

    public int OfferBackgroundDim { get; set; } = 75;

    public OfferBackgroundFocus OfferBackgroundFocus { get; set; } = OfferBackgroundFocus.Center;

    // Navigation properties
    public ApplicationUser? ReviewedByUser { get; set; }
    public ICollection<CompanyPhoto> Photos { get; set; } = new List<CompanyPhoto>();
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
