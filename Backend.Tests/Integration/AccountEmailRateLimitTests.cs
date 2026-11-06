using System.Net;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Aplikacja z domyślnym limitem e-maili bezpieczeństwa (RateLimiting:AccountEmails - 5 żądań w oknie 15 minut)
public class DefaultAccountEmailsRateLimitFixture : ApiFixture
{
    public DefaultAccountEmailsRateLimitFixture()
    {
        // null - brak nadpisania z JobBoardFactory, obowiązuje wartość domyślna aplikacji
        Factory.WithSetting(JobBoardFactory.AccountEmailsPermitLimitKey, null);
    }
}

// forgot-password - limit per adres IP (wszystkie żądania testowe mają ten sam adres), resend-confirmation - per
// użytkownik; przekroczenie -> 429 z Retry-After i komunikatem API
public class AccountEmailRateLimitTests : IClassFixture<DefaultAccountEmailsRateLimitFixture>
{
    private const int PermitLimit = 5;

    private readonly DefaultAccountEmailsRateLimitFixture _api;

    public AccountEmailRateLimitTests(DefaultAccountEmailsRateLimitFixture api) => _api = api;

    [Fact]
    public async Task ResendConfirmation_IsLimitedPerUser()
    {
        var user = await _api.RegisterUserAsync("limit");
        var other = await _api.RegisterUserAsync("limit");
        using var client = _api.CreateClient(user);
        using var otherClient = _api.CreateClient(other);

        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsync("/api/auth/resend-confirmation", null)).StatusCode);
        }

        var limited = await client.PostAsync("/api/auth/resend-confirmation", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal("Too many requests - try again later", await limited.ReadMessageAsync());
        var retryAfter = int.Parse(Assert.Single(limited.Headers.GetValues("Retry-After")));
        Assert.InRange(retryAfter, 1, 900);

        // inny użytkownik ma własny limit
        Assert.Equal(HttpStatusCode.NoContent, (await otherClient.PostAsync("/api/auth/resend-confirmation", null)).StatusCode);
    }

    [Fact]
    public async Task ForgotPassword_IsLimitedPerAddress_AndOtherEndpointsAreNot()
    {
        using var anonymous = _api.CreateClient();
        var email = $"nobody_{Guid.NewGuid():N}@test.local";

        // wspólny adres IP z testem powyżej nie ma znaczenia - ten test zużywa okno tylko forgot-password
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i <= PermitLimit; i++)
        {
            statuses.Add((await anonymous.ForgotPasswordAsync(email)).StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Accepted, PermitLimit).Append(HttpStatusCode.TooManyRequests), statuses);

        // logowanie i rejestracja nie są objęte tym limitem
        Assert.NotEmpty(await _api.LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword));
        await _api.RegisterUserAsync("limit");
    }
}
