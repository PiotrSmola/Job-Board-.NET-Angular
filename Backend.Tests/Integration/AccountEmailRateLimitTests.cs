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

// forgot-password - limit per adres IP (żądania testowe bez X-Forwarded-For mają ten sam adres), resend-confirmation - per
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

    // forgot-password liczy się per adres IP także z tokenem w żądaniu - tokeny kilku kont nie dają osobnych pul;
    // limit forgot-password nie zabiera okna resend-confirmation (per użytkownik) ani logowania i rejestracji
    [Fact]
    public async Task ForgotPassword_IsLimitedPerAddressEvenWithAToken_AndOtherEndpointsAreNot()
    {
        var first = await _api.RegisterUserAsync("limit");
        var second = await _api.RegisterUserAsync("limit");
        using var anonymous = _api.CreateClient();
        using var firstClient = _api.CreateClient(first);
        using var secondClient = _api.CreateClient(second);
        HttpClient[] clients = [anonymous, firstClient, secondClient];
        var email = $"nobody_{Guid.NewGuid():N}@test.local";

        // wspólny adres IP z testem powyżej nie ma znaczenia - ten test zużywa okno tylko forgot-password
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < PermitLimit; i++)
        {
            statuses.Add((await clients[i % clients.Length].ForgotPasswordAsync(email)).StatusCode);
        }

        Assert.Equal(Enumerable.Repeat(HttpStatusCode.Accepted, PermitLimit), statuses);
        foreach (var client in clients)
        {
            Assert.Equal(HttpStatusCode.TooManyRequests, (await client.ForgotPasswordAsync(email)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.NoContent, (await firstClient.PostAsync("/api/auth/resend-confirmation", null)).StatusCode);

        // logowanie i rejestracja nie są objęte tym limitem
        Assert.NotEmpty(await _api.LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword));
        await _api.RegisterUserAsync("limit");
    }

    // adres IP to adres klienta z X-Forwarded-For dopisanego przez nginx (UseForwardedHeaders) - klienci za tym samym
    // proxy mają osobne limity
    [Fact]
    public async Task ForgotPassword_IsLimitedPerForwardedClientAddress()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        using var client = _api.CreateClient();
        client.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{bytes[0]}.{bytes[1]}.{bytes[2]}");
        using var other = _api.CreateClient();
        other.DefaultRequestHeaders.Add("X-Forwarded-For", $"10.{bytes[0]}.{bytes[1]}.{(byte)(bytes[2] + 1)}");
        var email = $"nobody_{Guid.NewGuid():N}@test.local";

        for (var i = 0; i < PermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Accepted, (await client.ForgotPasswordAsync(email)).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.ForgotPasswordAsync(email)).StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, (await other.ForgotPasswordAsync(email)).StatusCode);
    }
}
