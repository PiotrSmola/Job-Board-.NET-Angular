using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

public class JobApplication
{
    public int Id { get; set; }

    [Required]
    public int JobOfferId { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? Note { get; set; }

    public DateTime AppliedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public JobOffer JobOffer { get; set; } = null!;
    public ApplicationUser User { get; set; } = null!;
}
