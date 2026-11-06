using System.Net;
using Backend.DTOs;
using Backend.Services.Email;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Potwierdzenie adresu e-mail: e-mail z linkiem po rejestracji (Base64Url), POST confirm-email z danymi z linku,
// ponowne wysłanie dla zalogowanego, emailConfirmed w UserDto i profilu
[Collection(ApiCollection.Name)]
public class EmailConfirmationTests
{
    private const string InvalidLink = "Invalid or expired confirmation link";

    private readonly ApiFixture _api;

    public EmailConfirmationTests(ApiFixture api) => _api = api;

    private async Task<bool> EmailConfirmedAsync(string userId) =>
        await _api.QueryDbAsync(db => db.Users.Where(u => u.Id == userId).Select(u => u.EmailConfirmed).SingleAsync());

    [Fact]
    public async Task Register_ReturnsEmailConfirmedFalseAndSendsALinkThatConfirmsTheAddress()
    {
        var unique = Guid.NewGuid().ToString("N");
        var email = $"confirm_{unique}@test.local";
        using var anonymous = _api.CreateClient();
        var response = await anonymous.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "Ewa", LastName = "Test" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.False(user.EmailConfirmed);

        var (link, userId, token) = await _api.Emails.ConfirmationLinkAsync(email);
        var message = _api.Emails.SentTo(email).Single();
        Assert.Contains("Cześć Ewa,", message.TextBody);
        Assert.Equal("Ewa Test", message.ToName);
        // link prowadzi do frontendu z konfiguracji, token bez znaków, które router Angulara by zepsuł
        Assert.StartsWith(JobBoardFactory.FrontendBaseUrl + "/confirm-email?userId=", link.ToString());
        Assert.Equal(JwtTestHelper.GetUserId(user.Token), userId);
        Assert.Matches("^[A-Za-z0-9_-]+$", token);

        var confirm = await anonymous.ConfirmEmailAsync(userId, token);
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
        Assert.True(await EmailConfirmedAsync(userId));

        // potwierdzenie nie unieważnia wydanych tokenów; logowanie zwraca już emailConfirmed = true
        using var client = _api.CreateClient(user.Token);
        var profile = await (await client.GetAsync(AccountApi.ProfileUrl)).ReadJsonAsync<ProfileDto>();
        Assert.True(profile.EmailConfirmed);
        var login = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = "Passw0rd1" });
        Assert.True((await login.ReadJsonAsync<UserDto>()).EmailConfirmed);

        // ponowne użycie ważnego linku też kończy się sukcesem
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ConfirmEmailAsync(userId, token)).StatusCode);
    }

    [Fact]
    public async Task RegisterEmployer_AlsoSendsTheConfirmationLink()
    {
        var employer = await _api.RegisterEmployerAsync();

        var (_, userId, token) = await _api.Emails.ConfirmationLinkAsync(employer.Email);

        Assert.Equal(employer.Id, userId);
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NoContent, (await anonymous.ConfirmEmailAsync(userId, token)).StatusCode);
    }

    [Fact]
    public async Task Login_OfSeededAccount_ReturnsEmailConfirmedTrue()
    {
        using var anonymous = _api.CreateClient();
        var response = await anonymous.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.UserEmail, Password = TestUsers.UserPassword });

        Assert.True((await response.ReadJsonAsync<UserDto>()).EmailConfirmed);
    }

    [Fact]
    public async Task ConfirmEmail_WithAWrongTokenOrUser_Returns400AndDoesNotConfirm()
    {
        var user = await _api.RegisterUserAsync("confirm");
        var (_, userId, token) = await _api.Emails.ConfirmationLinkAsync(user.Email);
        var other = await _api.RegisterUserAsync("confirm");
        var (_, _, otherToken) = await _api.Emails.ConfirmationLinkAsync(other.Email);
        using var anonymous = _api.CreateClient();

        var cases = new (string UserId, string Token)[]
        {
            (userId, otherToken),                                   // token innego konta
            (Guid.NewGuid().ToString(), token),                     // nieistniejące konto
            (userId, token[..^4]),                                  // ucięty token
            (userId, "not base64!"),                                // nie Base64Url
            (userId, EmailTokens.Encode("garbage")),                // poprawny Base64Url, zły token
            (userId, Uri.UnescapeDataString(EmailTokens.Encode("x")) + "+/=")
        };

        foreach (var (caseUserId, caseToken) in cases)
        {
            var response = await anonymous.ConfirmEmailAsync(caseUserId, caseToken);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(InvalidLink, await response.ReadMessageAsync());
        }

        Assert.False(await EmailConfirmedAsync(userId));
    }

    [Fact]
    public async Task ConfirmEmail_WithMissingOrTooLongFields_Returns400()
    {
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.BadRequest, (await anonymous.PostRawJsonAsync("/api/auth/confirm-email", "{}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.ConfirmEmailAsync(new string('a', 129), "abc")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,
            (await anonymous.ConfirmEmailAsync(Guid.NewGuid().ToString(), new string('a', 2049))).StatusCode);
    }

    [Fact]
    public async Task ConfirmationLink_StopsWorkingAfterAPasswordChange()
    {
        // token Identity zawiera security stamp - zmiana hasła unieważnia wcześniejsze linki
        var user = await _api.RegisterUserAsync("confirm");
        var (_, userId, token) = await _api.Emails.ConfirmationLinkAsync(user.Email);
        using var client = _api.CreateClient(user);
        Assert.Equal(HttpStatusCode.OK, (await client.ChangePasswordAsync(user.Password, "NewPassw0rd")).StatusCode);

        using var anonymous = _api.CreateClient();
        var response = await anonymous.ConfirmEmailAsync(userId, token);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(InvalidLink, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task ResendConfirmation_SendsANewWorkingLink()
    {
        var user = await _api.RegisterUserAsync("resend");
        await _api.Emails.ConfirmationLinkAsync(user.Email);
        using var client = _api.CreateClient(user);

        var response = await client.PostAsync("/api/auth/resend-confirmation", null);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        await _api.Emails.WaitForAsync(user.Email, _ => _api.Emails.SentTo(user.Email).Count == 2);
        var (_, userId, token) = await _api.Emails.ConfirmationLinkAsync(user.Email);
        Assert.Equal(HttpStatusCode.NoContent, (await client.ConfirmEmailAsync(userId, token)).StatusCode);
    }

    [Fact]
    public async Task ResendConfirmation_WhenAlreadyConfirmed_Returns400WithoutAnEmail()
    {
        var user = await _api.RegisterUserAsync("resend");
        var (_, userId, token) = await _api.Emails.ConfirmationLinkAsync(user.Email);
        using var client = _api.CreateClient(user);
        Assert.Equal(HttpStatusCode.NoContent, (await client.ConfirmEmailAsync(userId, token)).StatusCode);

        var response = await client.PostAsync("/api/auth/resend-confirmation", null);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Email is already confirmed", await response.ReadMessageAsync());
        await _api.Emails.AssertNothingSentAsync(user.Email, alreadySent: 1);
    }

    [Fact]
    public async Task ResendConfirmation_RequiresAuthentication()
    {
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsync("/api/auth/resend-confirmation", null)).StatusCode);
    }

    [Fact]
    public async Task FailedRegistration_DoesNotLeaveAConfirmationEmailInTheOutbox()
    {
        // słabe hasło - konto, rola i e-mail w jednej transakcji, więc nic nie zostaje
        var email = $"weak_{Guid.NewGuid():N}@test.local";
        using var anonymous = _api.CreateClient();
        var response = await anonymous.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "weakpass", FirstName = "A", LastName = "B" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.False(await _api.QueryDbAsync(db => db.EmailMessages.AnyAsync(m => m.ToAddress == email)));
        await _api.Emails.AssertNothingSentAsync(email, wait: TimeSpan.FromSeconds(1));
    }
}
