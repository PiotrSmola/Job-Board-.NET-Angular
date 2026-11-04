using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Wpis w historii aplikacji: zmiana statusu albo notatka rekrutera (ten sam status z komentarzem)
public class ApplicationStatusHistory
{
    public int Id { get; set; }

    public int JobApplicationId { get; set; }

    public ApplicationStatus Status { get; set; }

    public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

    // null po usunięciu konta autora zmiany
    public string? ChangedByUserId { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    // Navigation properties
    public JobApplication JobApplication { get; set; } = null!;
    public ApplicationUser? ChangedByUser { get; set; }
}
