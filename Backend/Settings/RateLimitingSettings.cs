namespace Backend.Settings;

// Ograniczanie liczby żądań (sekcja "RateLimiting"):
// - polityka "uploads" - upload plików (załączniki, dokumenty profilu), osobny limit dla każdego zalogowanego
//   użytkownika i wspólny limit równoległych uploadów,
// - polityka "account-emails" - żądania wysyłające e-maile bezpieczeństwa (reset hasła - per adres IP, ponowne
//   wysłanie potwierdzenia - per użytkownik)
public class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";
    public const string UploadsPolicy = "uploads";
    public const string AccountEmailsPolicy = "account-emails";
    public const string RejectedMessage = "Too many requests - try again later";

    public RateLimitRule Uploads { get; set; } = new();

    public RateLimitWindow AccountEmails { get; set; } = new() { PermitLimit = 5, WindowSeconds = 900 };

    // rzuca InvalidOperationException przy złej konfiguracji (przerywa start aplikacji)
    public void Validate()
    {
        Uploads.Validate($"{SectionName}:Uploads");
        AccountEmails.Validate($"{SectionName}:AccountEmails");
    }
}

// Najwyżej PermitLimit żądań w oknie WindowSeconds na partycję (okno stałe liczone od pierwszego żądania;
// Retry-After = czas do końca okna)
public class RateLimitWindow
{
    public int PermitLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 600;

    public TimeSpan Window => TimeSpan.FromSeconds(WindowSeconds);

    public virtual void Validate(string prefix)
    {
        if (PermitLimit < 1)
        {
            throw new InvalidOperationException($"{prefix}:PermitLimit must be a positive number (got {PermitLimit}).");
        }

        if (WindowSeconds < 1)
        {
            throw new InvalidOperationException($"{prefix}:WindowSeconds must be a positive number (got {WindowSeconds}).");
        }
    }
}

// Okno na użytkownika (jak RateLimitWindow) i najwyżej ConcurrencyLimit żądań naraz łącznie dla wszystkich
// użytkowników (bez kolejki - nadmiarowe dostają od razu 429)
public class RateLimitRule : RateLimitWindow
{
    public int ConcurrencyLimit { get; set; } = 8;

    public override void Validate(string prefix)
    {
        base.Validate(prefix);

        if (ConcurrencyLimit < 1)
        {
            throw new InvalidOperationException($"{prefix}:ConcurrencyLimit must be a positive number (got {ConcurrencyLimit}).");
        }
    }
}
