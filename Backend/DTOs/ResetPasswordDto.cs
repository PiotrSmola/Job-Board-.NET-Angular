using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

// Nowe hasło z linku z e-maila (token zakodowany Base64Url, jak w linku)
public class ResetPasswordDto
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string Token { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}
