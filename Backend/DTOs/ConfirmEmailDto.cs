using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

// Potwierdzenie adresu z linku z e-maila (token zakodowany Base64Url, jak w linku)
public class ConfirmEmailDto
{
    [Required]
    [MaxLength(128)]
    public string UserId { get; set; } = string.Empty;

    [Required]
    [MaxLength(2048)]
    public string Token { get; set; } = string.Empty;
}
