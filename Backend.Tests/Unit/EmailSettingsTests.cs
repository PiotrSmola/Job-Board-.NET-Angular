using Backend.Interfaces;
using Backend.Services.Email;
using Backend.Settings;

namespace Backend.Tests.Unit;

// Walidacja ustawień e-maili i adresu frontendu (zła konfiguracja przerywa start aplikacji), backoff outboksu
public class EmailSettingsTests
{
    [Theory]
    [InlineData("http://localhost:4200", "http://localhost:4200")]
    [InlineData("http://localhost:4201/", "http://localhost:4201")]
    [InlineData("https://jobs.example.test/app/", "https://jobs.example.test/app")]
    [InlineData("", FrontendSettings.DefaultBaseUrl)]
    public void Frontend_AcceptsAbsoluteHttpUrlsWithoutTrailingSlash(string value, string expected)
    {
        var settings = new FrontendSettings { BaseUrl = value };

        settings.Validate();

        Assert.Equal(expected, settings.BaseUrl);
    }

    [Theory]
    [InlineData("localhost:4200")]
    [InlineData("/relative")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://user:pass@example.com")]
    [InlineData("http://example.com/?next=x")]
    [InlineData("http://example.com/#x")]
    public void Frontend_RejectsOtherUrls(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new FrontendSettings { BaseUrl = value }.Validate());
        Assert.Contains("Frontend:BaseUrl", exception.Message);
    }

    [Fact]
    public void Email_Defaults_AreValidAndWithoutSmtp()
    {
        var settings = new EmailSettings();

        settings.Validate();

        Assert.False(settings.IsSmtpConfigured);
        Assert.Equal("no-reply@jobboard.local", settings.FromAddress);
        Assert.Equal(8, settings.Outbox.MaxAttempts);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not an address")]
    [InlineData("a@b.c, d@e.f")]
    [InlineData("Job Board <no-reply@jobboard.local>")]
    public void Email_RejectsAnInvalidFromAddress(string from)
    {
        var exception = Assert.Throws<InvalidOperationException>(() => new EmailSettings { FromAddress = from }.Validate());
        Assert.Contains("Email:FromAddress", exception.Message);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65536)]
    public void Smtp_RejectsAPortOutOfRange(int port)
    {
        var settings = new EmailSettings { Smtp = { Host = "smtp.example.com", Port = port } };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("Email:Smtp:Port", exception.Message);
    }

    [Fact]
    public void Smtp_RequiresAPasswordWithAUserName()
    {
        var settings = new EmailSettings { Smtp = { Host = "smtp.example.com", UserName = "user" } };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("Email:Smtp:Password", exception.Message);
    }

    [Theory]
    [InlineData(nameof(EmailOutboxSettings.PollIntervalSeconds))]
    [InlineData(nameof(EmailOutboxSettings.BatchSize))]
    [InlineData(nameof(EmailOutboxSettings.MaxAttempts))]
    [InlineData(nameof(EmailOutboxSettings.SendTimeoutSeconds))]
    [InlineData(nameof(EmailOutboxSettings.RetentionDays))]
    public void Outbox_RejectsNonPositiveValues(string property)
    {
        var settings = new EmailSettings();
        typeof(EmailOutboxSettings).GetProperty(property)!.SetValue(settings.Outbox, 0);

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains($"Email:Outbox:{property}", exception.Message);
    }

    [Fact]
    public void Outbox_RetryDelay_DoublesUpToTheMaximum()
    {
        var outbox = new EmailOutboxSettings { InitialRetryDelaySeconds = 30, MaxRetryDelaySeconds = 3600 };

        Assert.Equal(TimeSpan.FromSeconds(30), outbox.RetryDelay(1));
        Assert.Equal(TimeSpan.FromSeconds(60), outbox.RetryDelay(2));
        Assert.Equal(TimeSpan.FromSeconds(120), outbox.RetryDelay(3));
        Assert.Equal(TimeSpan.FromSeconds(1920), outbox.RetryDelay(7));
        Assert.Equal(TimeSpan.FromSeconds(3600), outbox.RetryDelay(8));
        Assert.Equal(TimeSpan.FromSeconds(3600), outbox.RetryDelay(1000));
    }

    [Fact]
    public void Outbox_RejectsAMaximumDelaySmallerThanTheInitialOne()
    {
        var settings = new EmailSettings { Outbox = { InitialRetryDelaySeconds = 60, MaxRetryDelaySeconds = 30 } };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);
        Assert.Contains("Email:Outbox:MaxRetryDelaySeconds", exception.Message);
    }

    [Fact]
    public void Describe_IncludesInnerExceptionsAndIsAtMost500Characters()
    {
        var error = EmailOutboxProcessor.Describe(new InvalidOperationException("outer", new TimeoutException("inner")));
        Assert.Equal("InvalidOperationException: outer -> TimeoutException: inner", error);

        var longError = EmailOutboxProcessor.Describe(new EmailRejectedException(new string('x', 1000)));
        Assert.Equal(EmailOutboxProcessor.MaxErrorLength, longError.Length);
    }

    [Fact]
    public void RateLimiting_AccountEmails_DefaultsTo5RequestsPer15Minutes()
    {
        var settings = new RateLimitingSettings();
        settings.Validate();

        Assert.Equal(5, settings.AccountEmails.PermitLimit);
        Assert.Equal(900, settings.AccountEmails.WindowSeconds);
        Assert.Throws<InvalidOperationException>(() =>
            new RateLimitingSettings { AccountEmails = { PermitLimit = 0 } }.Validate());
    }
}
