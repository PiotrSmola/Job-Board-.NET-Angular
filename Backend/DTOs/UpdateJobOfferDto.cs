using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class UpdateJobOfferDto : JobOfferFormDto
{
    // brak pola = status bez zmian (edycja treści nie publikuje szkicu ani nie wznawia zamkniętej oferty)
    [EnumDataType(typeof(JobOfferStatus))]
    public JobOfferStatus? Status { get; set; }

    // Ta sama data ważności co zapisana (z tolerancją poniżej sekundy - klienci JS przesyłają tylko milisekundy)
    public bool KeepsExpiresAtOf(JobOffer job)
    {
        var expiresAt = GetExpiresAtUtc();
        return expiresAt.HasValue == job.ExpiresAt.HasValue &&
               (!expiresAt.HasValue || Math.Abs((expiresAt.Value - job.ExpiresAt!.Value).TotalSeconds) < 1);
    }

    // Walidacja daty względem zapisanej oferty (status może pochodzić z bazy): aktywna oferta musi mieć przyszłą
    // datę ważności, ale wygasłą aktywną ofertę można edytować bez zmiany daty - błąd tylko przy nowej dacie
    // albo przejściu na Active (publikacja szkicu, wznowienie zamkniętej)
    public ValidationResult? ValidateExpiresAtAgainst(JobOffer current, DateTime now)
    {
        var status = Status ?? current.Status;
        if (status != JobOfferStatus.Active || GetExpiresAtUtc() is not { } expiresAt || expiresAt > now)
        {
            return null;
        }

        return current.Status == JobOfferStatus.Active && KeepsExpiresAtOf(current) ? null : ExpiresAtInPastError();
    }
}
