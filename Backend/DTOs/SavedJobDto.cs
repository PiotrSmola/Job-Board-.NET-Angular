namespace Backend.DTOs;

// Pozycja listy zapisanych ofert (także oferty, które w międzyczasie wygasły albo zamknięto)
public class SavedJobDto
{
    public DateTime SavedAt { get; set; }
    public JobOfferDto Job { get; set; } = null!;
}
