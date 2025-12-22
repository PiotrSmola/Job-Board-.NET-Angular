using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

public class CreateJobApplicationDto
{
    [Required]
    public int JobOfferId { get; set; }

    [MaxLength(1000)]
    public string? Note { get; set; }
}
