namespace Backend.DTOs;

public class CategoryDto
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public string Slug { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    // oferty widoczne publicznie (aktywne, niewygasłe, firma zatwierdzona) - tyle samo, ile pokazują facety bez filtrów
    public int ActiveJobsCount { get; set; }
}
