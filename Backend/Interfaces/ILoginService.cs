using Backend.Models;

namespace Backend.Interfaces;

public enum LoginStatus
{
    Succeeded,
    InvalidCredentials,
    LockedOut
}

// Wynik sprawdzenia danych logowania; User tylko dla Succeeded
public sealed record LoginResult(LoginStatus Status, ApplicationUser? User = null);

public interface ILoginService
{
    Task<LoginResult> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken = default);
}
