using System.ComponentModel.DataAnnotations;

namespace Backend.Helpers;

// Parametry listy ofert w panelu admina (GET /api/jobs/manage)
public class ManageJobParams : JobParams
{
    public const string Draft = "draft";
    public const string Active = "active";
    public const string Expired = "expired";
    public const string Closed = "closed";

    // draft | active (aktywne i niewygasłe) | expired | closed; brak = wszystkie
    // \z zamiast $ - $ dopuszcza końcowy znak nowej linii ("draft\n")
    [RegularExpression(@"^(?i:draft|active|expired|closed)\z",ErrorMessage = "State must be one of: draft, active, expired, closed")]
    public string? State { get; set; }
}
