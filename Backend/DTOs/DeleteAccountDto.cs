using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

// Potwierdzenie usunięcia konta hasłem (body DELETE /api/account)
public class DeleteAccountDto
{
    [Required]
    [MaxLength(100)]
    public string Password { get; set; } = string.Empty;
}
