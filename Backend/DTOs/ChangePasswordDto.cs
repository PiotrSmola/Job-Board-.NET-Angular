using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

public class ChangePasswordDto
{
    [Required]
    [MaxLength(CredentialLimits.MaxPasswordLength)]
    public string CurrentPassword { get; set; } = string.Empty;

    // reguły siły hasła sprawdza Identity (błędy pod kluczem NewPassword)
    [Required]
    [MaxLength(CredentialLimits.MaxPasswordLength)]
    public string NewPassword { get; set; } = string.Empty;
}
