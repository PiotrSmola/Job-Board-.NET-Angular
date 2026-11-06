using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

// Potwierdzenie usunięcia konta hasłem (body DELETE /api/account)
public class DeleteAccountDto
{
    [Required]
    [MaxLength(CredentialLimits.MaxPasswordLength)]
    public string Password { get; set; } = string.Empty;
}
