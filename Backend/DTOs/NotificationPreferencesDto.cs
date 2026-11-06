using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

// Preferencje powiadomień e-mail (GET/PUT /api/account/notifications); w PUT wymagane wszystkie pola
public class NotificationPreferencesDto
{
    // kandydat: potwierdzenie złożenia aplikacji i zmiany jej statusu
    [Required]
    public bool? ApplicationStatusChanged { get; set; }

    // pracodawca: nowa aplikacja do oferty firmy
    [Required]
    public bool? NewApplication { get; set; }

    // pracodawca: decyzja administratora o firmie
    [Required]
    public bool? CompanyReview { get; set; }
}
