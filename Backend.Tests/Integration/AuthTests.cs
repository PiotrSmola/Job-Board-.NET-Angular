using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

[Collection(ApiCollection.Name)]
public class AuthTests
{
    private readonly ApiFixture _api;

    public AuthTests(ApiFixture api) => _api = api;

    private static RegisterDto NewRegisterDto(string password = "Passw0rd1") => new()
    {
        Email = $"auth_{Guid.NewGuid():N}@test.local",
        Password = password,
        FirstName = "Anna",
        LastName = "Testowa"
    };

    // --- rejestracja ---

    [Fact]
    public async Task Register_WithValidData_ReturnsUserWithTokenAndUserRole()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();

        var response = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.Equal(dto.Email, user.Email);
        Assert.Equal("Anna", user.FirstName);
        Assert.Equal("Testowa", user.LastName);
        Assert.False(string.IsNullOrWhiteSpace(user.Token));
        Assert.Equal(new[] { "User" }, JwtTestHelper.GetRoles(user.Token));
    }

    [Fact]
    public async Task Register_ThenLoginWithSameCredentials_Succeeds()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();
        await client.PostJsonAsync("/api/auth/register", dto);

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = dto.Email, Password = dto.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithDuplicateEmail_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();
        var first = await client.PostJsonAsync("/api/auth/register", dto);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);

        var second = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, second.StatusCode);
        using var body = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
        Assert.Equal("Email is already taken", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Register_WithDuplicateEmailInDifferentCase_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();
        await client.PostJsonAsync("/api/auth/register", dto);

        dto.Email = dto.Email.ToUpperInvariant();
        var response = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithSeededAdminEmail_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();
        dto.Email = TestUsers.AdminEmail;

        var response = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("alllowercase1")]   // brak wielkiej litery
    [InlineData("ALLUPPERCASE1")]   // brak małej litery
    [InlineData("NoDigitsHere")]    // brak cyfry
    public async Task Register_WithPasswordViolatingIdentityRules_ReturnsBadRequestWithIdentityErrors(string password)
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/register", NewRegisterDto(password));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.NotEmpty(body.RootElement.EnumerateArray());
    }

    [Fact]
    public async Task Register_WithTooShortPassword_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/register", NewRegisterDto("Ab1"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithInvalidEmailFormat_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();
        dto.Email = "not-an-email";

        var response = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Register_WithMissingNames_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();
        var dto = NewRegisterDto();
        dto.FirstName = string.Empty;

        var response = await client.PostJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- logowanie ---

    [Fact]
    public async Task Login_AsSeededAdmin_ReturnsTokenWithAdminRole()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.AdminEmail, Password = TestUsers.AdminPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.Equal(TestUsers.AdminEmail, user.Email);
        Assert.Contains("Admin", JwtTestHelper.GetRoles(user.Token));
    }

    [Fact]
    public async Task Login_AsSeededUser_ReturnsTokenWithUserRoleOnly()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.UserEmail, Password = TestUsers.UserPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.Equal("Jan", user.FirstName);
        Assert.Equal("Kowalski", user.LastName);
        Assert.Equal(new[] { "User" }, JwtTestHelper.GetRoles(user.Token));
    }

    [Fact]
    public async Task Login_IsCaseInsensitiveForEmail()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.UserEmail.ToUpperInvariant(), Password = TestUsers.UserPassword });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_WithWrongPassword_ReturnsUnauthorized()
    {
        var user = await _api.RegisterUserAsync("wrongpass");
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = "WrongPassw0rd" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid email or password", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_WithUnknownEmail_ReturnsUnauthorizedWithSameMessage()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = $"nobody_{Guid.NewGuid():N}@test.local", Password = "Passw0rd1" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Invalid email or password", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_WithMissingPassword_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = TestUsers.UserEmail, Password = string.Empty });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- blokada konta ---

    [Fact]
    public async Task Login_AfterFiveFailedAttempts_LocksAccount()
    {
        var user = await _api.RegisterUserAsync("lockout");
        using var client = _api.CreateClient();
        var wrong = new LoginDto { Email = user.Email, Password = "WrongPassw0rd" };

        // pierwsze cztery próby to zwykłe 401
        for (var attempt = 1; attempt <= 4; attempt++)
        {
            var failed = await client.PostJsonAsync("/api/auth/login", wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        // piąta nieudana próba blokuje konto
        var fifth = await client.PostJsonAsync("/api/auth/login", wrong);

        Assert.Equal(HttpStatusCode.Locked, fifth.StatusCode);
        using var body = JsonDocument.Parse(await fifth.Content.ReadAsStringAsync());
        Assert.Contains("locked", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Login_WithCorrectPasswordWhileLocked_StillReturnsLocked()
    {
        var user = await _api.RegisterUserAsync("lockedcorrect");
        using var client = _api.CreateClient();
        var wrong = new LoginDto { Email = user.Email, Password = "WrongPassw0rd" };
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostJsonAsync("/api/auth/login", wrong);
        }

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = user.Password });

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
    }

    [Fact]
    public async Task Login_LockoutDoesNotAffectOtherAccounts()
    {
        var locked = await _api.RegisterUserAsync("lockedone");
        var other = await _api.RegisterUserAsync("otherone");
        using var client = _api.CreateClient();
        for (var attempt = 0; attempt < 5; attempt++)
        {
            await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = locked.Email, Password = "WrongPassw0rd" });
        }

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = other.Email, Password = other.Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Login_SuccessfulLoginResetsFailedAttemptCounter()
    {
        var user = await _api.RegisterUserAsync("resetcounter");
        using var client = _api.CreateClient();
        var wrong = new LoginDto { Email = user.Email, Password = "WrongPassw0rd" };
        var correct = new LoginDto { Email = user.Email, Password = user.Password };

        for (var attempt = 0; attempt < 3; attempt++)
        {
            await client.PostJsonAsync("/api/auth/login", wrong);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/login", correct)).StatusCode);

        // licznik wyzerowany - kolejne 3 błędy nie blokują konta (łącznie byłoby 6)
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var failed = await client.PostJsonAsync("/api/auth/login", wrong);
            Assert.Equal(HttpStatusCode.Unauthorized, failed.StatusCode);
        }

        Assert.Equal(HttpStatusCode.OK, (await client.PostJsonAsync("/api/auth/login", correct)).StatusCode);
    }

    // Równoległe nieudane próby nie gubią inkrementacji licznika ani nie kończą się konfliktem zapisu (500):
    // blokada per konto szereguje sprawdzenie hasła, więc konto blokuje się dokładnie po 5 nieudanych próbach
    [Fact]
    public async Task Login_TwentyParallelFailedAttempts_LockTheAccountAfterFive()
    {
        var user = await _api.RegisterUserAsync("parallel");
        var wrong = new LoginDto { Email = user.Email, Password = "WrongPassw0rd" };

        var responses = await Task.WhenAll(Enumerable.Range(0, 20).Select(async _ =>
        {
            using var client = _api.CreateClient();
            return (await client.PostJsonAsync("/api/auth/login", wrong)).StatusCode;
        }));

        Assert.Equal(4, responses.Count(s => s == HttpStatusCode.Unauthorized));
        Assert.Equal(16, responses.Count(s => s == HttpStatusCode.Locked));
        var account = await _api.QueryDbAsync(db => db.Users.AsNoTracking().SingleAsync(u => u.Id == user.Id));
        Assert.True(account.LockoutEnd > DateTimeOffset.UtcNow.AddMinutes(4), $"Account should be locked, LockoutEnd = {account.LockoutEnd}");
        Assert.Equal(0, account.AccessFailedCount);
    }
}
