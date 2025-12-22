using System.ComponentModel.DataAnnotations;
using Backend.Models;

namespace Backend.DTOs;

public class CreateJobOfferDto
{
    [Required]
    [MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required]
    [MaxLength(200)]
    public string Company { get; set; } = string.Empty;

    [Required]
    [MaxLength(5000)]
    public string Description { get; set; } = string.Empty;

    public decimal? SalaryMin { get; set; }

    public decimal? SalaryMax { get; set; }

    [Required]
    [MaxLength(200)]
    public string Location { get; set; } = string.Empty;

    public WorkType WorkType { get; set; }
}
