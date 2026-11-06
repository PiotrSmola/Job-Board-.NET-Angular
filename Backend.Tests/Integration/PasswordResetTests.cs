using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Services.Email;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Reset hasła: forgot-password zawsze 202 (bez ujawniania kont), e-mail z linkiem (e-mail i token Base64Url),
// reset z danymi z linku unieważnia stare tokeny JWT i sam link; słabe hasło - ValidationProblem z opisami Identity
[Collection(ApiCollection.Name)]
public class PasswordResetTests
{
    private const string InvalidLink = "Invalid or expired reset link";
    private const string NewPassword = "Brand-New-Passw0rd";

    private readonly ApiFixture _api;

    public PasswordResetTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task ResetFromTheEmailLink_ChangesThePasswordAndRevokesOldTokens()
    {
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();

        var forgot = await anonymous.ForgotPasswordAsync(user.Email);
        Assert.Equal(HttpStatusCode.Accepted, forgot.StatusCode);

        var (link, email, token) = await _api.Emails.ResetLinkAsync(user.Email);
        Assert.StartsWith(JobBoardFactory.FrontendBaseUrl + "/reset-password?email=", link.ToString());
        Assert.Equal(user.Email, email);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);

        var reset = await anonymous.ResetPasswordAsync(email, token, NewPassword);
        Assert.Equal(HttpStatusCode.NoContent, reset.StatusCode);

        // stary token (security stamp sprzed resetu) - 401
        using var oldClient = _api.CreateClient(user);
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync(AccountApi.ProfileUrl)).StatusCode);

        // stare hasło nie działa, nowe tak
        var oldLogin = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, oldLogin.StatusCode);
        var newLogin = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, newLogin.StatusCode);

        // link z e-maila potwierdza też adres (przyszedł na ten adres)
        Assert.True((await newLogin.ReadJsonAsync<UserDto>()).EmailConfirmed);

        // ten sam link drugi raz już nie działa (nowy security stamp)
        var again = await anonymous.ResetPasswordAsync(email, token, "Another-Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, again.StatusCode);
        Assert.Equal(InvalidLink, await again.ReadMessageAsync());
    }

    [Fact]
    public async Task ForgotPassword_ForAnUnknownAddress_Returns202WithoutAnEmail()
    {
        var email = $"nobody_{Guid.NewGuid():N}@test.local";
        using var anonymous = _api.CreateClient();

        var response = await anonymous.ForgotPasswordAsync(email);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.False(await _api.QueryDbAsync(db => db.EmailMessages.AnyAsync(m => m.ToAddress == email)));
    }

    [Fact]
    public async Task ForgotPassword_WithAnInvalidAddress_Returns400()
    {
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.ForgotPasswordAsync("not-an-email")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostRawJsonAsync("/api/auth/forgot-password", "{}")).StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_SendsAtMostThreeEmailsPerAccountAndHour_ButAlwaysReturns202()
    {
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();

        for (var i = 0; i < EmailOutbox.MaxPasswordResetsPerHour + 2; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await anonymous.ForgotPasswordAsync(user.Email)).StatusCode);
        }

        var count = await _api.QueryDbAsync(db =>
            db.EmailMessages.CountAsync(m => m.UserId == user.Id && m.Type == EmailType.PasswordReset));
        Assert.Equal(EmailOutbox.MaxPasswordResetsPerHour, count);
    }

    [Fact]
    public async Task Reset_WithAWeakPassword_ReturnsValidationProblemWithIdentityDescriptions()
    {
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(user.Email);
        var (_, email, token) = await _api.Emails.ResetLinkAsync(user.Email);

        var response = await anonymous.ResetPasswordAsync(email, token, "weakpass");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors").GetProperty("NewPassword").EnumerateArray()
            .Select(e => e.GetString()).ToArray();
        // domyślne opisy IdentityErrorDescriber - frontend tłumaczy je po treści
        Assert.Contains(errors, e => e!.StartsWith("Passwords must have at least one digit"));
        Assert.Contains(errors, e => e!.StartsWith("Passwords must have at least one uppercase"));

        // słabe hasło nie zużyło linku
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ResetPasswordAsync(email, token, NewPassword)).StatusCode);
    }

    [Fact]
    public async Task Reset_WithAPasswordLongerThan100Characters_Returns400()
    {
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(user.Email);
        var (_, email, token) = await _api.Emails.ResetLinkAsync(user.Email);

        var response = await anonymous.ResetPasswordAsync(email, token, "Aa1" + new string('x', 98));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("NewPassword", await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Reset_WithAWrongTokenOrEmail_Returns400InvalidLink()
    {
        var user = await _api.RegisterUserAsync("reset");
        var other = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(user.Email);
        await anonymous.ForgotPasswordAsync(other.Email);
        var (_, _, token) = await _api.Emails.ResetLinkAsync(user.Email);
        var (_, _, otherToken) = await _api.Emails.ResetLinkAsync(other.Email);

        var cases = new (string Email, string Token)[]
        {
            (user.Email, otherToken),
            (other.Email, token),
            ($"nobody_{Guid.NewGuid():N}@test.local", token),
            (user.Email, "not base64!"),
            (user.Email, EmailTokens.Encode("garbage")),
            (user.Email, token[..^3])
        };

        foreach (var (email, caseToken) in cases)
        {
            var response = await anonymous.ResetPasswordAsync(email, caseToken, NewPassword);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(InvalidLink, await response.ReadMessageAsync());
        }

        // hasło bez zmian
        Assert.NotEmpty(await _api.LoginAsync(user.Email, user.Password));
    }

    [Fact]
    public async Task ResetLink_CreatedBeforeAnotherReset_IsNoLongerValid()
    {
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(user.Email);
        var (_, email, firstToken) = await _api.Emails.ResetLinkAsync(user.Email);
        await anonymous.ForgotPasswordAsync(user.Email);
        var (_, _, secondToken) = await _api.Emails.ResetLinkAsync(user.Email, expectedCount: 2);

        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ResetPasswordAsync(email, secondToken, NewPassword)).StatusCode);

        var response = await anonymous.ResetPasswordAsync(email, firstToken, "Another-Passw0rd");
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reset_UnlocksAnAccountLockedByFailedLogins()
    {
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();
        for (var i = 0; i < 5; i++)
        {
            await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = "Wrong-Passw0rd" });
        }

        var locked = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = user.Password });
        Assert.Equal(HttpStatusCode.Locked, locked.StatusCode);

        await anonymous.ForgotPasswordAsync(user.Email);
        var (_, email, token) = await _api.Emails.ResetLinkAsync(user.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ResetPasswordAsync(email, token, NewPassword)).StatusCode);

        var login = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = NewPassword });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
    }

    [Fact]
    public async Task ResetEmail_IsCleanedFromTheOutboxAfterSending()
    {
        // link z tokenem nie zostaje w bazie po wysłaniu
        var user = await _api.RegisterUserAsync("reset");
        using var anonymous = _api.CreateClient();
        await anonymous.ForgotPasswordAsync(user.Email);
        await _api.Emails.ResetLinkAsync(user.Email);

        var message = await WaitForStatusAsync(user.Id, EmailType.PasswordReset, EmailMessageStatus.Sent);

        Assert.Null(message.HtmlBody);
        Assert.Null(message.TextBody);
        Assert.Equal(1, message.Attempts);
        Assert.NotNull(message.SentAt);
        Assert.Equal(user.Email, message.ToAddress);
    }

    private async Task<EmailMessage> WaitForStatusAsync(string userId, EmailType type, EmailMessageStatus status)
    {
        var deadline = DateTime.UtcNow.AddSeconds(15);
        while (true)
        {
            var message = await _api.QueryDbAsync(db => db.EmailMessages.AsNoTracking()
                .Where(m => m.UserId == userId && m.Type == type)
                .OrderByDescending(m => m.Id)
                .FirstAsync());
            if (message.Status == status || DateTime.UtcNow > deadline)
            {
                Assert.Equal(status, message.Status);
                return message;
            }

            await Task.Delay(100);
        }
    }
}
