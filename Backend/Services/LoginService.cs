using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Identity;

namespace Backend.Services;

// Blokady logowania per konto (singleton): równoległe próby na ten sam znormalizowany e-mail wykonują się po kolei
public sealed class LoginLocks
{
    public KeyedLock Accounts { get; } = new();
}

// Sprawdzenie danych logowania z blokadą konta po 5 nieudanych próbach (Identity lockout)
public class LoginService : ILoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly LoginLocks _locks;

    public LoginService(UserManager<ApplicationUser> userManager, SignInManager<ApplicationUser> signInManager, LoginLocks locks)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _locks = locks;
    }

    public async Task<LoginResult> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // Odczyt konta i zapis licznika nieudanych prób pod blokadą konta: bez niej równoległe próby czytały ten sam
        // licznik (zgubione inkrementacje - więcej niż 5 prób przed blokadą) albo kończyły się konfliktem
        // współbieżności przy zapisie (500)
        using var _ = await _locks.Accounts.AcquireAsync(_userManager.NormalizeEmail(email), cancellationToken);

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return new LoginResult(LoginStatus.LockedOut);
        }

        return result.Succeeded ? new LoginResult(LoginStatus.Succeeded, user) : new LoginResult(LoginStatus.InvalidCredentials);
    }
}
