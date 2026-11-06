using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class ChangePasswordDto
{
    [Required]
    [MaxLength(100)]
    public string CurrentPassword { get; set; } = string.Empty;

    // reguły siły hasła sprawdza Identity (błędy pod kluczem NewPassword)
    [Required]
    [MaxLength(100)]
    public string NewPassword { get; set; } = string.Empty;
}
