using System.Net;
using Backend.Interfaces;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

// Aplikacja z krótkim backoffem outboksu (1 s, najwyżej 3 próby) - ponawianie bez długiego czekania
public class FastRetryOutboxFixture : ApiFixture
{
    public const int MaxAttempts = 3;

    public FastRetryOutboxFixture()
    {
        Factory.WithSetting("Email:Outbox:InitialRetryDelaySeconds", "1")
            .WithSetting("Email:Outbox:MaxRetryDelaySeconds", "1")
            .WithSetting("Email:Outbox:MaxAttempts", MaxAttempts.ToString());
    }
}

// Outbox: nieudane wysyłki są ponawiane z opóźnieniem, po limicie prób albo trwałym odrzuceniu - Failed z błędem;
// treść czyszczona po wysłaniu i ostatecznym błędzie
public class EmailOutboxTests : IClassFixture<FastRetryOutboxFixture>
{
    private readonly FastRetryOutboxFixture _api;

    public EmailOutboxTests(FastRetryOutboxFixture api) => _api = api;

    private async Task<EmailMessage> WaitForFinalStateAsync(string userId)
    {
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (true)
        {
            var message = await _api.QueryDbAsync(db => db.EmailMessages.AsNoTracking()
                .Where(m => m.UserId == userId && m.Type == EmailType.EmailConfirmation)
                .SingleAsync());
            if (message.Status != EmailMessageStatus.Pending)
            {
                return message;
            }

            Assert.True(DateTime.UtcNow < deadline, $"E-mail still pending after {message.Attempts} attempt(s): {message.LastError}");
            await Task.Delay(100);
        }
    }

    private int AttemptsTo(string address) => _attemptsByAddress.TryGetValue(address, out var count) ? count : 0;

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _attemptsByAddress = new();

    // Rejestruje użytkownika, którego e-maile kończą się podanym błędem w pierwszych failures próbach
    private async Task<TestUser> RegisterWithFailuresAsync(int failures, Func<Exception> error)
    {
        var prefix = $"outbox{Guid.NewGuid():N}";
        var previous = _api.Emails.Failure;
        _api.Emails.Failure = email =>
        {
            if (!email.ToAddress.StartsWith(prefix, StringComparison.Ordinal))
            {
                return previous?.Invoke(email);
            }

            var attempt = _attemptsByAddress.AddOrUpdate(email.ToAddress, 1, (_, count) => count + 1);
            return attempt <= failures ? error() : null;
        };

        return await _api.RegisterUserAsync(prefix);
    }

    [Fact]
    public async Task FailedSends_AreRetriedUntilTheySucceed()
    {
        var user = await RegisterWithFailuresAsync(2, () => new IOException("Connection refused"));

        var message = await WaitForFinalStateAsync(user.Id);

        Assert.Equal(EmailMessageStatus.Sent, message.Status);
        Assert.Equal(3, message.Attempts);
        Assert.Null(message.LastError);
        Assert.Null(message.HtmlBody);
        Assert.Null(message.TextBody);
        Assert.Equal(3, AttemptsTo(user.Email));
        Assert.Single(_api.Emails.SentTo(user.Email));
        // kolejna próba nie wcześniej niż po opóźnieniu (1 s po każdej z dwóch porażek)
        Assert.True(message.SentAt - message.CreatedAt >= TimeSpan.FromSeconds(2), $"{message.SentAt - message.CreatedAt}");
    }

    [Fact]
    public async Task AfterTheLastAttempt_TheMessageFailsWithTheErrorAndWithoutContent()
    {
        var user = await RegisterWithFailuresAsync(int.MaxValue, () => new IOException("Connection refused"));

        var message = await WaitForFinalStateAsync(user.Id);

        Assert.Equal(EmailMessageStatus.Failed, message.Status);
        Assert.Equal(FastRetryOutboxFixture.MaxAttempts, message.Attempts);
        Assert.Equal("IOException: Connection refused", message.LastError);
        Assert.Null(message.HtmlBody);
        Assert.Null(message.TextBody);
        Assert.Null(message.SentAt);

        // po ostatecznym błędzie nie ma kolejnych prób
        await Task.Delay(TimeSpan.FromSeconds(2.5));
        Assert.Equal(FastRetryOutboxFixture.MaxAttempts, AttemptsTo(user.Email));
    }

    [Fact]
    public async Task PermanentRejection_FailsAfterOneAttempt()
    {
        var user = await RegisterWithFailuresAsync(int.MaxValue, () => new EmailRejectedException("SMTP server rejected the message (550)"));

        var message = await WaitForFinalStateAsync(user.Id);

        Assert.Equal(EmailMessageStatus.Failed, message.Status);
        Assert.Equal(1, message.Attempts);
        Assert.StartsWith("EmailRejectedException: SMTP server rejected the message (550)", message.LastError);
    }

    [Fact]
    public async Task OtherMessages_AreSentWhileOneIsRetried()
    {
        var failing = await RegisterWithFailuresAsync(int.MaxValue, () => new IOException("Connection refused"));
        var healthy = await _api.RegisterUserAsync("outbox-ok");

        await _api.Emails.WaitForAsync(healthy.Email);

        Assert.Empty(_api.Emails.SentTo(failing.Email));
    }

    [Fact]
    public async Task OldFinishedMessages_AreRemovedFromTheOutbox_PendingOnesStay()
    {
        // sprzątanie działa przy starcie procesora i potem co godzinę - stare wiadomości wstawione do bazy, a potem
        // aplikacja uruchomiona ponownie na tej samej bazie
        var factory = new JobBoardFactory();
        try
        {
            _ = factory.Server;
            var user = await RegisterAsync(factory);
            await factory.EmailSender!.WaitForAsync(user.Email);
            var old = DateTime.UtcNow.AddDays(-31);
            await using (var scope = factory.Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<Backend.Data.ApplicationDbContext>();
                db.EmailMessages.AddRange(
                    Message(user.Id, EmailMessageStatus.Sent, old),
                    Message(user.Id, EmailMessageStatus.Failed, old),
                    Message(user.Id, EmailMessageStatus.Pending, old, nextAttemptAt: DateTime.UtcNow.AddDays(1)));
                await db.SaveChangesAsync();
            }

            await factory.DisposeAsync();

            // własna baza tej fabryki nie powstaje (connection string nadpisany) - usuwany jest tylko jej katalog plików
            var restarted = new JobBoardFactory().WithSetting("ConnectionStrings:DefaultConnection", factory.ConnectionString);
            try
            {
                _ = restarted.Server;

                var deadline = DateTime.UtcNow.AddSeconds(15);
                List<EmailMessageStatus> statuses;
                do
                {
                    await Task.Delay(200);
                    await using var scope = restarted.Services.CreateAsyncScope();
                    var db = scope.ServiceProvider.GetRequiredService<Backend.Data.ApplicationDbContext>();
                    var before = DateTime.UtcNow.AddDays(-30);
                    statuses = await db.EmailMessages.Where(m => m.UserId == user.Id && m.CreatedAt < before)
                        .Select(m => m.Status).ToListAsync();
                } while (statuses.Count > 1 && DateTime.UtcNow < deadline);

                Assert.Equal([EmailMessageStatus.Pending], statuses);
                // nowe wiadomości (wysłane) zostają
                Assert.True(await RecentSentAsync(restarted, user.Id));
            }
            finally
            {
                await restarted.DisposeAndDropDatabaseAsync();
            }
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    private static async Task<bool> RecentSentAsync(JobBoardFactory factory, string userId)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<Backend.Data.ApplicationDbContext>();
        return await db.EmailMessages.AnyAsync(m => m.UserId == userId && m.Type == EmailType.EmailConfirmation
                                                    && m.Status == EmailMessageStatus.Sent);
    }

    private static async Task<TestUser> RegisterAsync(JobBoardFactory factory)
    {
        var email = $"retention_{Guid.NewGuid():N}@test.local";
        using var client = factory.CreateClient();
        var response = await client.PostJsonAsync("/api/auth/register",
            new Backend.DTOs.RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "R", LastName = "T" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.ReadJsonAsync<Backend.DTOs.UserDto>();
        return new TestUser(email, "Passw0rd1", "R", "T", dto.Token, JwtTestHelper.GetUserId(dto.Token));
    }

    private static EmailMessage Message(string userId, EmailMessageStatus status, DateTime createdAt, DateTime? nextAttemptAt = null) => new()
    {
        UserId = userId,
        Type = EmailType.ApplicationSubmitted,
        ToAddress = "old@test.local",
        Subject = "Old",
        HtmlBody = status == EmailMessageStatus.Pending ? "<p>x</p>" : null,
        TextBody = status == EmailMessageStatus.Pending ? "x" : null,
        Status = status,
        CreatedAt = createdAt,
        NextAttemptAt = nextAttemptAt ?? createdAt
    };
}
