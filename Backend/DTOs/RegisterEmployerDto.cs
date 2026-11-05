using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

// Rejestracja pracodawcy razem z firmą (firma czeka na weryfikację admina)
public class RegisterEmployerDto : RegisterDto
{
    [Required]
    [MaxLength(200)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string? CompanyDescription { get; set; }

    [MaxLength(300)]
    [HttpUrl]
    public string? Website { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }
}
