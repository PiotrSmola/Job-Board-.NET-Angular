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

    // walidacja między polami - uruchamiana po walidacji atrybutów
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (SalaryMin.HasValue && SalaryMax.HasValue && SalaryMin.Value > SalaryMax.Value)
        {
            yield return new ValidationResult(
                "SalaryMin cannot be greater than SalaryMax",
                new[] { nameof(SalaryMin), nameof(SalaryMax) });
        }
    }
}
