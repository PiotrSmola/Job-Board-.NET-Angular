using System.Text.RegularExpressions;
using System.Web;
using Backend.Interfaces;

namespace Backend.Tests.Infrastructure;

// Zamiast SMTP w testach: zapamiętuje wysłane wiadomości; Failure pozwala symulować błędy serwera pocztowego
public sealed class FakeEmailSender : IEmailSender
{
    private readonly object _lock = new();
    private readonly List<OutgoingEmail> _sent = [];
    private int _attempts;

    // wyjątek dla danej próby (null - wysłano); domyślnie każda próba się udaje
    public Func<OutgoingEmail, Exception?>? Failure { get; set; }

    public int Attempts => Volatile.Read(ref _attempts);

    public IReadOnlyList<OutgoingEmail> Sent
    {
        get
        {
            lock (_lock)
            {
                return _sent.ToList();
            }
        }
    }

    public Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        Interlocked.Increment(ref _attempts);
        var failure = Failure?.Invoke(email);
        if (failure != null)
        {
            throw failure;
        }

        lock (_lock)
        {
            _sent.Add(email);
        }

        return Task.CompletedTask;
    }

    public IReadOnlyList<OutgoingEmail> SentTo(string address) =>
        Sent.Where(e => string.Equals(e.ToAddress, address, StringComparison.OrdinalIgnoreCase)).ToList();

    // Czeka (odpytując) na wiadomość do adresu spełniającą warunek
    public async Task<OutgoingEmail> WaitForAsync(string address, Func<OutgoingEmail, bool>? predicate = null,
        TimeSpan? timeout = null)
    {
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromSeconds(15));
        while (true)
        {
            var match = SentTo(address).LastOrDefault(e => predicate?.Invoke(e) ?? true);
            if (match != null)
            {
                return match;
            }

            if (DateTime.UtcNow > deadline)
            {
                Assert.Fail($"No e-mail to {address} was sent in time (sent: {string.Join(", ", Sent.Select(e => $"{e.ToAddress}: {e.Subject}"))})");
            }

            await Task.Delay(50);
        }
    }

    // Upewnia się, że do adresu nic (więcej) nie wysłano - po odczekaniu na kilka cykli outboksu
    public async Task AssertNothingSentAsync(string address, int alreadySent = 0, TimeSpan? wait = null)
    {
        await Task.Delay(wait ?? TimeSpan.FromSeconds(2));
        Assert.Equal(alreadySent, SentTo(address).Count);
    }

    // Pierwszy link z tekstowej wersji wiadomości zaczynający się od prefiksu
    public static Uri LinkFrom(OutgoingEmail email, string prefix)
    {
        var match = Regex.Match(email.TextBody, Regex.Escape(prefix) + @"\S*");
        Assert.True(match.Success, $"No link starting with {prefix} in: {email.TextBody}");
        return new Uri(match.Value);
    }

    // Parametr zapytania linku (zdekodowany jak przez przeglądarkę)
    public static string QueryValue(Uri link, string name) =>
        HttpUtility.ParseQueryString(link.Query)[name] ?? throw new InvalidOperationException($"No {name} in {link}");
}
