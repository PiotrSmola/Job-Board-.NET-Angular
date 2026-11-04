namespace Backend.Helpers;

public class ApplicationParams : PaginationParams
{
    // Filtrowanie: email, imię/nazwisko kandydata, tytuł oferty, firma
    public string? SearchTerm { get; set; }
    public int? JobOfferId { get; set; }

    // Sortowanie: newest (domyślnie), oldest
    public string OrderBy { get; set; } = "newest";
}
