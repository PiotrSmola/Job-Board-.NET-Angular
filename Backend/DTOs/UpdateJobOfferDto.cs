using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class UpdateJobOfferDto : JobOfferFormDto
{
    // brak pola = status bez zmian (edycja treści nie publikuje szkicu ani nie wznawia zamkniętej oferty)
    [EnumDataType(typeof(JobOfferStatus))]
    public JobOfferStatus? Status { get; set; }

    // Walidacja daty względem zapisanej oferty (status może pochodzić z bazy): aktywna oferta musi mieć przyszłą datę ważności
    public ValidationResult? ValidateExpiresAtAgainst(JobOffer current, DateTime now)
    {
        var status = Status ?? current.Status;
        return status == JobOfferStatus.Active && GetExpiresAtUtc() is { } expiresAt && expiresAt <= now
            ? ExpiresAtInPastError()
            : null;
    }
}
