using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

// Edycja profilu (e-mail nie jest zmieniany tym endpointem); wartości są przycinane, puste pola opcjonalne = null.
// \z zamiast $ - $ dopuszcza końcowy znak nowej linii
public class UpdateProfileDto
{
    // [Required] odrzuca też same spacje
    [Required]
    [MaxLength(100)]
    public string FirstName { get; set; } = string.Empty;

    [Required]
    [MaxLength(100)]
    public string LastName { get; set; } = string.Empty;

    [MaxLength(30)]
    [RegularExpression(@"^[0-9+\- ]+\z", ErrorMessage = "Phone number may contain only digits, spaces, '+' and '-'")]
    public string? PhoneNumber { get; set; }

    [MaxLength(150)]
    public string? Headline { get; set; }

    [MaxLength(200)]
    public string? Location { get; set; }

    [MaxLength(300)]
    [RegularExpression(@"^(?i:https://(www\.)?linkedin\.com/\S+)\z",
        ErrorMessage = "LinkedIn URL must start with https://linkedin.com/ or https://www.linkedin.com/")]
    public string? LinkedInUrl { get; set; }

    [MaxLength(2000)]
    public string? About { get; set; }
}
