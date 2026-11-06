using System.ComponentModel.DataAnnotations;
using Backend.Helpers;
using Backend.Models;

namespace Backend.DTOs;

// Pola oferty wspólne dla tworzenia (POST) i edycji (PUT); status i reguły daty ważności są w klasach pochodnych
public abstract class JobOfferFormDto : IValidatableObject
{
    // górna granica wynika z kolumny decimal(18,2)
    public const string MaxSalary = "9999999999999999.99";

    public const string ExpiresAtInPastMessage = "ExpiresAt must be in the future for an active job offer";

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    // firma oferty: wymagana dla admina (brak - błąd walidacji, nieistniejąca firma - 400), ignorowana dla
    // pracodawcy - jego oferty zawsze należą do jego firmy
    public int? CompanyId { get; set; }

    [Required]
    [MaxLength(5000)]
    public string Description { get; set; } = string.Empty;

    [Range(typeof(decimal), "0", MaxSalary, ParseLimitsInInvariantCulture = true)]
    public decimal? SalaryMin { get; set; }

    [Range(typeof(decimal), "0", MaxSalary, ParseLimitsInInvariantCulture = true)]
    public decimal? SalaryMax { get; set; }

    [Required]
    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;

    // System.Text.Json przyjmuje dowolną liczbę dla enuma - EnumDataType odrzuca wartości spoza WorkType
    [EnumDataType(typeof(WorkType))]
    public WorkType WorkType { get; set; }

    // data bez strefy traktowana jest jako UTC (patrz GetExpiresAtUtc)
    public DateTime? ExpiresAt { get; set; }

    // Taksonomia (PUT zastępuje wszystkie pola: pominięte wracają do wartości domyślnych).
    // Kategoria musi istnieć (sprawdza kontroler: 400 "Category not found"); null = bez kategorii
    public int? CategoryId { get; set; }

    [EnumDataType(typeof(SeniorityLevel))]
    public SeniorityLevel? Seniority { get; set; }

    // maska bitowa typów umów (0 = nie podano); bity spoza ContractTypes odrzuca Validate
    public ContractTypes ContractTypes { get; set; }

    [EnumDataType(typeof(Currency))]
    public Currency SalaryCurrency { get; set; }

    [EnumDataType(typeof(SalaryPeriod))]
    public SalaryPeriod SalaryPeriod { get; set; }

    // 0-15 umiejętności, każda 1-50 znaków po normalizacji i bez przecinka; nowe są tworzone automatycznie,
    // dopasowanie bez wielkości liter, duplikaty są scalane (patrz GetSkillNames)
    public List<string> Skills { get; set; } = new();

    public DateTime? GetExpiresAtUtc() => ExpiresAt.HasValue ? ToUtc(ExpiresAt.Value) : null;

    // Nazwy umiejętności po normalizacji, bez duplikatów (bez wielkości liter; zostaje pierwsze wystąpienie)
    public List<string> GetSkillNames() =>
        Skills.Where(s => s != null).Select(SkillNames.Normalize)
            .DistinctBy(SkillNames.KeyOf)
            .ToList();

    // walidacja między polami - uruchamiana po walidacji atrybutów
    public virtual IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin.Value > SalaryMax.Value)
        {
            yield return new ValidationResult(
                "SalaryMin cannot be greater than SalaryMax",
                new[] { nameof(SalaryMin), nameof(SalaryMax) });
        }

        if (!ContractTypesExtensions.IsValidMask((int)ContractTypes))
        {
            yield return new ValidationResult("ContractTypes contains an unknown contract type", new[] { nameof(ContractTypes) });
        }

        if (Skills.Count > SkillNames.MaxPerOffer)
        {
            yield return new ValidationResult($"A job offer can have at most {SkillNames.MaxPerOffer} skills", new[] { nameof(Skills) });
        }
        else if (Skills.Any(s => s == null || !SkillNames.IsValid(SkillNames.Normalize(s))))
        {
            yield return new ValidationResult(
                $"Each skill must have 1-{SkillNames.MaxLength} characters and must not contain a comma",
                new[] { nameof(Skills) });
        }
    }

    protected static ValidationResult ExpiresAtInPastError() =>
        new(ExpiresAtInPastMessage, new[] { nameof(ExpiresAt) });

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
