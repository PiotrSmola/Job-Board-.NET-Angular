namespace Backend.Models;

// Oferta zapisana przez kandydata (klucz: użytkownik + oferta)
public class SavedJob
{
    public string UserId { get; set; } = string.Empty;

    public int JobOfferId { get; set; }

    // UTC
    public DateTime SavedAt { get; set; }

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
    public JobOffer JobOffer { get; set; } = null!;
}
