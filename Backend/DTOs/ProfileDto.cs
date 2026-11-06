namespace Backend.DTOs;

// Profil zalogowanego użytkownika (GET/PUT /api/account/profile)
public class ProfileDto
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string? PhoneNumber { get; set; }
    public string? Headline { get; set; }
    public string? Location { get; set; }
    public string? LinkedInUrl { get; set; }
    public string? About { get; set; }
    public bool EmailConfirmed { get; set; }
    public IReadOnlyList<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }
}
