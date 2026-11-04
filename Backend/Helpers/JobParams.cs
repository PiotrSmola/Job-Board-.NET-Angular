using Backend.Models;

namespace Backend.Helpers;

public class JobParams : PaginationParams
{
    // Filtrowanie
    public string? SearchTerm { get; set; }
    public WorkType? WorkType { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string? Location { get; set; }

    // Sortowanie: newest (domyślnie), oldest, salaryHigh, salaryLow,
    // titleAsc, titleDesc, companyAsc, companyDesc, locationAsc, locationDesc
    public string OrderBy { get; set; } = "newest";
}
