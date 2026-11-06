using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.Helpers;

public class JobParams : PaginationParams, IValidatableObject
{
    // Filtrowanie (wszystkie opcjonalne, łączone AND; tekst do 100 znaków - dłuższy to 400, nie kosztowne LIKE)
    [MaxLength(MaxSearchLength)]
    public string? SearchTerm { get; set; }
    // lista po przecinku (workType=0,1 = OR); pojedyncza liczba lub nazwa nadal działa
    [MaxLength(50)]
    public string? WorkType { get; set; }
    public decimal? MinSalary { get; set; }
    public decimal? MaxSalary { get; set; }
    [MaxLength(MaxSearchLength)]
    public string? Location { get; set; }
    public int? CompanyId { get; set; }

    // slug kategorii; \z zamiast $ - $ dopuszcza końcowy znak nowej linii
    [MaxLength(100)]
    [RegularExpression(@"^[a-z0-9-]+\z", ErrorMessage = "Category must be a slug: lowercase letters, digits and hyphens")]
    public string? Category { get; set; }
    public int? CategoryId { get; set; }

    // poziomy po przecinku (seniority=1,2 = OR)
    [MaxLength(50)]
    public string? Seniority { get; set; }

    // maska bitowa typów umów: oferta pasuje, gdy ma KTÓRYKOLWIEK z zaznaczonych typów (0 = brak filtra)
    [Range(0, ContractTypesExtensions.AllFlags)]
    public int? ContractTypes { get; set; }

    // nazwy umiejętności po przecinku (oferta musi mieć WSZYSTKIE), maks. JobFilterParser.MaxSkills po 50 znaków
    [MaxLength(JobFilterParser.MaxSkills * (SkillNames.MaxLength + 1))]
    public string? Skills { get; set; }

    // waluta widełek (0 = PLN jest realnym filtrem); minSalary/maxSalary w walucie oferty, bez przeliczania
    [EnumDataType(typeof(Currency))]
    public Currency? Currency { get; set; }

    // Sortowanie: newest (domyślnie), oldest, salaryHigh, salaryLow,
    // titleAsc, titleDesc, companyAsc, companyDesc, locationAsc, locationDesc
    public string OrderBy { get; set; } = "newest";

    // Listy po przecinku sprawdzane po walidacji atrybutów; sparsowany filtr buduje JobFilterParser.Parse
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext) =>
        JobFilterParser.Parse(this).Errors;
}
