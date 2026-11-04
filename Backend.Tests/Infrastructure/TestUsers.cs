namespace Backend.Tests.Infrastructure;

// Konta z seeda (DatabaseSeeder)
public static class TestUsers
{
    public const string AdminEmail = "admin@jobboard.com";
    public const string AdminPassword = "Admin123!";

    public const string UserEmail = "jan.kowalski@example.com";
    public const string UserPassword = "User123!";
}

// Użytkownik zarejestrowany przez API na potrzeby jednego testu
public sealed record TestUser(string Email, string Password, string FirstName, string LastName, string Token, string Id);
