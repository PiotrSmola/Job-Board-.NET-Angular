namespace Backend.Models;

// Stan zapisany w bazie; "wygasła" to stan wyliczany (Active + ExpiresAt w przeszłości)
public enum JobOfferStatus
{
    Draft = 0,
    Active = 1,
    Closed = 2
}
