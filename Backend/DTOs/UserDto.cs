namespace Backend.DTOs;

public class UserDto
{
    public string Email { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string LastName { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;

    // frontend pokazuje baner "Potwierdź adres e-mail" przy false
    public bool EmailConfirmed { get; set; }
}
