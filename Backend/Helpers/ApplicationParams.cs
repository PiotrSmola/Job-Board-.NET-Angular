using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.Helpers;

public class ApplicationParams : PaginationParams
{
    // Filtrowanie: email, imię/nazwisko kandydata, tytuł oferty, nazwa firmy
    public string? SearchTerm { get; set; }
    public int? JobOfferId { get; set; }

    [EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus? Status { get; set; }

    // Sortowanie: newest (domyślnie), oldest
    public string OrderBy { get; set; } = "newest";
}
