using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class CreateJobOfferDto : JobOfferFormDto
{
    // brak pola = oferta od razu aktywna (zgodnie z dotychczasowym zachowaniem API)
    [EnumDataType(typeof(JobOfferStatus))]
    public JobOfferStatus Status { get; set; } = JobOfferStatus.Active;

    public override IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        foreach (var result in base.Validate(validationContext))
        {
            yield return result;
        }

        if (Status == JobOfferStatus.Active && GetExpiresAtUtc() is { } expiresAt && expiresAt <= DateTime.UtcNow)
        {
            yield return ExpiresAtInPastError();
        }
    }
}
