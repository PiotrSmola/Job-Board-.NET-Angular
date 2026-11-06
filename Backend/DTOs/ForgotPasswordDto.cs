using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class ForgotPasswordDto
{
    [Required]
    [EmailAddress]
    [MaxLength(256)]
    public string Email { get; set; } = string.Empty;
}
