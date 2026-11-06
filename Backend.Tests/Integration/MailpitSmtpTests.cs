using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;
using Backend.DTOs;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Test uruchamiany tylko z zewnętrznym Mailpitem (SMTP na porcie 1025 tego samego hosta):
// JOBBOARD_TEST_MAILPIT_URL=http://jobboard-dev-mailpit:8025
public sealed class MailpitFactAttribute : FactAttribute
{
    public MailpitFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(MailpitSmtpTests.ApiUrl))
        {
            Skip = $"Set {MailpitSmtpTests.UrlVariable} (e.g. http://jobboard-dev-mailpit:8025) to run SMTP tests";
        }
    }
}

// Prawdziwa wysyłka przez SmtpEmailSender (MailKit) do Mailpita: wiadomość multipart (HTML + tekst) z polskimi
// znakami w temacie, link potwierdzający z odebranej wiadomości działa w API
public sealed class MailpitSmtpTests : IAsyncLifetime
{
    public const string UrlVariable = "JOBBOARD_TEST_MAILPIT_URL";
    public const int SmtpPort = 1025;

    public static string? ApiUrl => Environment.GetEnvironmentVariable(UrlVariable);

    private JobBoardFactory? _factory;

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        if (_factory != null)
        {
            await _factory.DisposeAndDropDatabaseAsync();
        }
    }

    [MailpitFact]
    public async Task Register_DeliversAConfirmationEmailWhoseLinkConfirmsTheAddress()
    {
        var api = new Uri(ApiUrl!);
        _factory = new JobBoardFactory().UseRealEmailSender(api.Host, SmtpPort);
        _ = _factory.Server;

        var email = $"mailpit_{Guid.NewGuid():N}@test.local";
        using var client = _factory.CreateClient();
        var register = await client.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "Łucja", LastName = "Żółć" });
        Assert.Equal(HttpStatusCode.OK, register.StatusCode);

        using var mailpit = new HttpClient { BaseAddress = api };
        var message = await WaitForMessageAsync(mailpit, email);

        Assert.Equal("Potwierdź adres e-mail w Job Board", message.GetProperty("Subject").GetString());
        Assert.Equal("no-reply@jobboard.local", message.GetProperty("From").GetProperty("Address").GetString());
        Assert.Equal("Łucja Żółć", message.GetProperty("To")[0].GetProperty("Name").GetString());
        var text = message.GetProperty("Text").GetString()!;
        var html = message.GetProperty("HTML").GetString()!;
        Assert.Contains("Cześć Łucja,", text);
        Assert.Contains("<html lang=\"pl\">", html);

        var link = new Uri(Regex.Match(text, Regex.Escape(JobBoardFactory.FrontendBaseUrl + "/confirm-email") + @"\S+").Value);
        var confirm = await client.ConfirmEmailAsync(FakeEmailSender.QueryValue(link, "userId"), FakeEmailSender.QueryValue(link, "token"));
        Assert.Equal(HttpStatusCode.NoContent, confirm.StatusCode);
    }

    private static async Task<JsonElement> WaitForMessageAsync(HttpClient mailpit, string address)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var search = await mailpit.GetStringAsync($"/api/v1/search?query={Uri.EscapeDataString($"to:\"{address}\"")}");
            using var result = JsonDocument.Parse(search);
            var messages = result.RootElement.GetProperty("messages");
            if (messages.GetArrayLength() > 0)
            {
                var id = messages[0].GetProperty("ID").GetString();
                using var message = JsonDocument.Parse(await mailpit.GetStringAsync($"/api/v1/message/{id}"));
                return message.RootElement.Clone();
            }

            Assert.True(DateTime.UtcNow < deadline, $"No e-mail to {address} arrived in Mailpit");
            await Task.Delay(250);
        }
    }
}
