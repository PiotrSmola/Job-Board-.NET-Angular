using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class RejectCompanyDto
{
    // powód jest widoczny dla pracodawcy w profilu firmy
    [Required]
    [MaxLength(1000)]
    public string Reason { get; set; } = string.Empty;
}
