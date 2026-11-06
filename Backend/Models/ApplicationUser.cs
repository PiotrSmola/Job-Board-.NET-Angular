using Microsoft.AspNetCore.Identity;

namespace Backend.Models;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // firma pracodawcy (rola Employer); null dla kandydatów i adminów
    public int? CompanyId { get; set; }

    // Navigation properties
    public Company? Company { get; set; }
    public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
}
