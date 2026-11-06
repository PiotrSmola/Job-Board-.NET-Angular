using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

public class LoginDto
{
    [Required]
    [EmailAddress]
    [MaxLength(CredentialLimits.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(CredentialLimits.MaxPasswordLength)]
    public string Password { get; set; } = string.Empty;
}
