using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class CreateJobOfferDto : IValidatableObject
{
    // górna granica wynika z kolumny decimal(18,2)
    public const string MaxSalary = "9999999999999999.99";

    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Company { get; set; } = string.Empty;

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

    // brak pola = oferta od razu aktywna (zgodnie z dotychczasowym zachowaniem API)
    [EnumDataType(typeof(JobOfferStatus))]
    public JobOfferStatus Status { get; set; } = JobOfferStatus.Active;

    // data bez strefy traktowana jest jako UTC (patrz GetExpiresAtUtc)
    public DateTime? ExpiresAt { get; set; }

    public DateTime? GetExpiresAtUtc() => ExpiresAt.HasValue ? ToUtc(ExpiresAt.Value) : null;

    // walidacja między polami - uruchamiana po walidacji atrybutów
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin.Value > SalaryMax.Value)
        {
            yield return new ValidationResult(
                "SalaryMin cannot be greater than SalaryMax",
                new[] { nameof(SalaryMin), nameof(SalaryMax) });
        }

        if (Status == JobOfferStatus.Active && GetExpiresAtUtc() is { } expiresAt && expiresAt <= DateTime.UtcNow)
        {
            yield return new ValidationResult(
                "ExpiresAt must be in the future for an active job offer",
                new[] { nameof(ExpiresAt) });
        }
    }

    private static DateTime ToUtc(DateTime value) => value.Kind switch
    {
        DateTimeKind.Utc => value,
        DateTimeKind.Local => value.ToUniversalTime(),
        _ => DateTime.SpecifyKind(value, DateTimeKind.Utc)
    };
}
