using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Identity;

namespace Backend.Services;

// Stan logowania wspólny dla żądań (singleton): blokady per konto i hasz-atrapa do wyrównania czasu odpowiedzi
public sealed class LoginGuard
{
    private string? _dummyPasswordHash;

    // równoległe próby na ten sam znormalizowany e-mail wykonują się po kolei
    public KeyedLock Accounts { get; } = new();

    // hasz losowego hasła liczony tym samym hasherem co hasła kont (te same parametry PBKDF2, więc ten sam koszt);
    // wyścig przy pierwszym użyciu jest nieszkodliwy - każdy wynik jest poprawny
    public string DummyPasswordHash(IPasswordHasher<ApplicationUser> hasher) =>
        _dummyPasswordHash ??= hasher.HashPassword(new ApplicationUser(), Guid.NewGuid().ToString("N"));
}

// Sprawdzenie danych logowania z blokadą konta po 5 nieudanych próbach (Identity lockout), bez ujawniania, czy konto
// o danym adresie istnieje: nieistniejące konto i złe hasło dają ten sam wynik w zbliżonym czasie (hasło jest zawsze
// weryfikowane), a blokada jest zgłaszana tylko po podaniu poprawnego hasła
public class LoginService : ILoginService
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IPasswordHasher<ApplicationUser> _passwordHasher;
    private readonly LoginGuard _guard;

    public LoginService(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IPasswordHasher<ApplicationUser> passwordHasher,
        LoginGuard guard)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _passwordHasher = passwordHasher;
        _guard = guard;
    }

    public async Task<LoginResult> CheckCredentialsAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // Odczyt konta i zapis licznika nieudanych prób pod blokadą konta: bez niej równoległe próby czytały ten sam
        // licznik (zgubione inkrementacje - więcej niż 5 prób przed blokadą) albo kończyły się konfliktem
        // współbieżności przy zapisie (500)
        using var _ = await _guard.Accounts.AcquireAsync(_userManager.NormalizeEmail(email), cancellationToken);

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            // weryfikacja na haszu-atrapie - odpowiedź dla nieistniejącego konta trwa tyle, co dla złego hasła
            _passwordHasher.VerifyHashedPassword(new ApplicationUser(), _guard.DummyPasswordHash(_passwordHasher), password);
            return new LoginResult(LoginStatus.InvalidCredentials);
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            // Zablokowane konto: 423 tylko przy poprawnym haśle (inaczej 401 jak dla złego hasła), więc odpowiedź nie
            // zdradza istnienia konta temu, kto nie zna hasła. Próby w czasie blokady nie zmieniają licznika.
            return await _userManager.CheckPasswordAsync(user, password)
                ? new LoginResult(LoginStatus.LockedOut)
                : new LoginResult(LoginStatus.InvalidCredentials);
        }

        // nieudana próba, która właśnie zablokowała konto (IsLockedOut), to nadal złe hasło - 401
        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);

        return result.Succeeded ? new LoginResult(LoginStatus.Succeeded, user) : new LoginResult(LoginStatus.InvalidCredentials);
    }
}
