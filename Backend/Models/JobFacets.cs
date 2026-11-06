namespace Backend.Models;

// Wynik liczników faset (projekcja z zapytań SQL); wartości z licznikiem 0 są pomijane
public class JobFacets
{
    public int Total { get; init; }
    public IReadOnlyList<FacetCategory> Categories { get; init; } = [];
    public IReadOnlyList<FacetCount> Seniority { get; init; } = [];
    public IReadOnlyList<FacetCount> ContractTypes { get; init; } = [];
    public IReadOnlyList<FacetCount> WorkTypes { get; init; } = [];
    public IReadOnlyList<SkillSummary> Skills { get; init; } = [];
}

public record FacetCategory(int Id, string Slug, string Name, int Count);

public record FacetCount(int Value, int Count);
