using MailKit.Security;
using MimeKit;

namespace Backend.Settings;

// Wysyłka e-maili (sekcja "Email"): serwer SMTP, nadawca i outbox (ponawianie w tle)
public class EmailSettings
{
    public const string SectionName = "Email";

    public SmtpSettings Smtp { get; set; } = new();

    public string FromAddress { get; set; } = "no-reply@jobboard.local";
    public string FromName { get; set; } = "Job Board";

    public EmailOutboxSettings Outbox { get; set; } = new();

    // brak hosta SMTP - wiadomości czekają w outboksie (procesor ich nie wysyła)
    public bool IsSmtpConfigured => !string.IsNullOrWhiteSpace(Smtp.Host);

    // Rzuca InvalidOperationException przy złej konfiguracji (przerywa start aplikacji)
    public void Validate()
    {
        if (!MailboxAddress.TryParse(FromAddress, out var from) || from.Address != FromAddress.Trim()
            || !from.Address.Contains('@'))
        {
            throw new InvalidOperationException($"{SectionName}:FromAddress must be a single e-mail address (got '{FromAddress}').");
        }

        FromAddress = from.Address;
        FromName = string.IsNullOrWhiteSpace(FromName) ? "Job Board" : FromName.Trim();

        if (IsSmtpConfigured)
        {
            Smtp.Validate($"{SectionName}:Smtp");
        }

        Outbox.Validate($"{SectionName}:Outbox");
    }
}

public class SmtpSettings
{
    public string? Host { get; set; }
    public int Port { get; set; } = 587;

    // None (np. lokalny Mailpit), StartTls, SslOnConnect, StartTlsWhenAvailable albo Auto (465 - SSL, inne - STARTTLS,
    // jeśli serwer go oferuje)
    public SecureSocketOptions Security { get; set; } = SecureSocketOptions.Auto;

    // puste - bez uwierzytelniania
    public string? UserName { get; set; }
    public string? Password { get; set; }

    public void Validate(string prefix)
    {
        Host = Host?.Trim();
        if (Port is < 1 or > 65535)
        {
            throw new InvalidOperationException($"{prefix}:Port must be between 1 and 65535 (got {Port}).");
        }

        if (!string.IsNullOrEmpty(UserName) && string.IsNullOrEmpty(Password))
        {
            throw new InvalidOperationException($"{prefix}:Password is required when {prefix}:UserName is set.");
        }
    }
}

// Ponawianie: kolejne próby po InitialRetryDelaySeconds * 2^(n-1), najwyżej MaxRetryDelaySeconds; po MaxAttempts
// nieudanych próbach wiadomość ma status Failed. Wysłane i nieudane wiadomości są usuwane po RetentionDays dniach.
public class EmailOutboxSettings
{
    public int PollIntervalSeconds { get; set; } = 10;
    public int BatchSize { get; set; } = 20;
    public int MaxAttempts { get; set; } = 8;
    public int InitialRetryDelaySeconds { get; set; } = 30;
    public int MaxRetryDelaySeconds { get; set; } = 3600;
    public int SendTimeoutSeconds { get; set; } = 30;
    public int RetentionDays { get; set; } = 30;

    public TimeSpan PollInterval => TimeSpan.FromSeconds(PollIntervalSeconds);
    public TimeSpan SendTimeout => TimeSpan.FromSeconds(SendTimeoutSeconds);
    public TimeSpan Retention => TimeSpan.FromDays(RetentionDays);

    // opóźnienie przed kolejną próbą po attempts nieudanych próbach (attempts >= 1)
    public TimeSpan RetryDelay(int attempts)
    {
        var exponent = Math.Clamp(attempts - 1, 0, 30);
        var seconds = Math.Min((double)InitialRetryDelaySeconds * Math.Pow(2, exponent), MaxRetryDelaySeconds);
        return TimeSpan.FromSeconds(seconds);
    }

    public void Validate(string prefix)
    {
        Positive(PollIntervalSeconds, nameof(PollIntervalSeconds));
        Positive(BatchSize, nameof(BatchSize));
        Positive(MaxAttempts, nameof(MaxAttempts));
        Positive(InitialRetryDelaySeconds, nameof(InitialRetryDelaySeconds));
        Positive(SendTimeoutSeconds, nameof(SendTimeoutSeconds));
        Positive(RetentionDays, nameof(RetentionDays));

        if (MaxRetryDelaySeconds < InitialRetryDelaySeconds)
        {
            throw new InvalidOperationException(
                $"{prefix}:MaxRetryDelaySeconds must not be smaller than {prefix}:InitialRetryDelaySeconds (got {MaxRetryDelaySeconds}).");
        }

        void Positive(int value, string name)
        {
            if (value < 1)
            {
                throw new InvalidOperationException($"{prefix}:{name} must be a positive number (got {value}).");
            }
        }
    }
}
