using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

public class ForgotPasswordDto
{
    [Required]
    [EmailAddress]
    [MaxLength(CredentialLimits.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;
}
