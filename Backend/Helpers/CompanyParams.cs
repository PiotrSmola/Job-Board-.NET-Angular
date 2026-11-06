using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.Helpers;

// Parametry publicznej listy firm (GET /api/companies) - tylko firmy zatwierdzone
public class CompanyParams : PaginationParams
{
    public const string NameAsc = "nameAsc";
    public const string NameDesc = "nameDesc";
    public const string JobsDesc = "jobsDesc";

    // Filtrowanie po nazwie i lokalizacji
    [MaxLength(MaxSearchLength)]
    public string? SearchTerm { get; set; }

    // Sortowanie: nameAsc (domyślnie), nameDesc, jobsDesc (najwięcej aktywnych ofert)
    public string OrderBy { get; set; } = NameAsc;
}

// Parametry listy firm w panelu admina (GET /api/admin/companies)
public class AdminCompanyParams : PaginationParams
{
    public const string Newest = "newest";
    public const string Oldest = "oldest";

    [MaxLength(MaxSearchLength)]
    public string? SearchTerm { get; set; }

    [EnumDataType(typeof(CompanyStatus))]
    public CompanyStatus? Status { get; set; }

    // Sortowanie: newest (domyślnie), oldest, nameAsc, nameDesc
    public string OrderBy { get; set; } = Newest;
}
