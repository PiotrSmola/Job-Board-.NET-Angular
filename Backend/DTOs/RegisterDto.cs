using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

public class RegisterDto
{
    [Required]
    [EmailAddress]
    [MaxLength(CredentialLimits.MaxEmailLength)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    [MaxLength(CredentialLimits.MaxPasswordLength)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;
}
