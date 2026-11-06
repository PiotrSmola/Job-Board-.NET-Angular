namespace Backend.Models;

// Kategoria z licznikiem ofert wyliczanym w SQL (projekcja - nie jest encją)
public class CategorySummary
{
    public Category Category { get; init; } = null!;

    // oferty widoczne publicznie: aktywne, niewygasłe, w firmie zatwierdzonej
    public int ActiveJobsCount { get; init; }
}
