using Microsoft.AspNetCore.Identity;

namespace Backend.Models;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation property
    public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
}
