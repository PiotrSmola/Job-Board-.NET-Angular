namespace Backend.DTOs;

// Liczniki dla panelu filtrów; licznik fasety X pomija filtr X (można rozszerzać wybór). Wartości z licznikiem 0 są pomijane.
public class JobFacetsDto
{
    // oferty spełniające wszystkie filtry
    public int Total { get; set; }

    public List<FacetCategoryDto> Categories { get; set; } = new();

    public List<FacetValueDto> Seniority { get; set; } = new();

    // value = pojedynczy bit typu umowy
    public List<FacetValueDto> ContractTypes { get; set; } = new();

    public List<FacetValueDto> WorkTypes { get; set; } = new();

    // najpopularniejsze umiejętności (maks. 20)
    public List<FacetSkillDto> Skills { get; set; } = new();
}

public class FacetCategoryDto
{
    public int Id { get; set; }
    public string Slug { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}

public class FacetValueDto
{
    public int Value { get; set; }
    public int Count { get; set; }
}

public class FacetSkillDto
{
    public string Name { get; set; } = string.Empty;
    public int Count { get; set; }
}
