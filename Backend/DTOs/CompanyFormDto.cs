using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

// Dane firmy edytowane przez pracodawcę (PUT /api/employer/company) i admina (POST/PUT /api/admin/companies)
public class CompanyFormDto
{
    [Required]
    [MaxLength(200)]
    [CompanyName]
    public string Name { get; set; } = string.Empty;

    [MaxLength(5000)]
    public string? Description { get; set; }

    [MaxLength(300)]
    [HttpUrl]
    public string? Website { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }
}
