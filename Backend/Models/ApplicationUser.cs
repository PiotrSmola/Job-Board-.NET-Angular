using Microsoft.AspNetCore.Identity;

namespace Backend.Models;

public class ApplicationUser : IdentityUser
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // firma pracodawcy (rola Employer); null dla kandydatów i adminów
    public int? CompanyId { get; set; }

    // profil (PUT /api/account/profile); telefon w PhoneNumber z IdentityUser
    public string? Headline { get; set; }
    public string? Location { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? About { get; set; }

    // preferencje powiadomień e-mail (domyślnie włączone; e-maile bezpieczeństwa wysyłane zawsze):
    // kandydat - złożenie aplikacji i zmiana jej statusu, pracodawca - nowa aplikacja i decyzja admina o firmie
    public bool NotifyApplicationStatusChanged { get; set; } = true;
    public bool NotifyNewApplication { get; set; } = true;
    public bool NotifyCompanyReview { get; set; } = true;

    // Navigation properties
    public Company? Company { get; set; }
    public ICollection<JobApplication> JobApplications { get; set; } = new List<JobApplication>();
    public ICollection<ProfileDocument> ProfileDocuments { get; set; } = new List<ProfileDocument>();
    public ICollection<SavedJob> SavedJobs { get; set; } = new List<SavedJob>();
}
