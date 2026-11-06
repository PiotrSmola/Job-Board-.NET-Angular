using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.Helpers;

public class JobParams : PaginationParams
{
    // Filtrowanie (tekst do 100 znaków - dłuższy to 400, nie kosztowne LIKE)
    [MaxLength(MaxSearchLength)]
    public string? SearchTerm { get; set; }
    public WorkType? WorkType { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    [MaxLength(MaxSearchLength)]
    public string? Location { get; set; }
    public int? CompanyId { get; set; }

    // Sortowanie: newest (domyślnie), oldest, salaryHigh, salaryLow,
    // titleAsc, titleDesc, companyAsc, companyDesc, locationAsc, locationDesc
    public string OrderBy { get; set; } = "newest";
}
