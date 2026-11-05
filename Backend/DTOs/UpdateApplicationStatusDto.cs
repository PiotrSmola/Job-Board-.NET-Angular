using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class UpdateApplicationStatusDto
{
    // nullable + Required: brak pola w żądaniu daje 400 zamiast cichego Submitted
    [Required]
    [EnumDataType(typeof(ApplicationStatus))]
    public ApplicationStatus? Status { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }
}
