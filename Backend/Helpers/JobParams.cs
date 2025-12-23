using Backend.Models;

namespace Backend.Helpers;

public class JobParams
{
    private const int MaxPageSize = 50;
    private int _pageSize = 10;

    public int PageNumber { get; set; } = 1;

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = (value > MaxPageSize) ? MaxPageSize : value;
    }

    // Filtering
    public string? SearchTerm { get; set; }
    public WorkType? WorkType { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    public string? Location { get; set; }

    // Sorting
    public string OrderBy { get; set; } = "newest";
}
