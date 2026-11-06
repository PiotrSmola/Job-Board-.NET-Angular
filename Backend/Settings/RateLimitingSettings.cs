namespace Backend.Settings;

// Ograniczanie liczby żądań (sekcja "RateLimiting"):
// - "uploads" - upload załączników, osobny limit dla każdego zalogowanego użytkownika i wspólny limit równoległych uploadów,
// - "auth" - logowanie i rejestracja (i każdy przyszły endpoint AuthController, np. reset hasła), osobny limit dla
//   każdego adresu IP (brute force) i wspólny limit równoległych żądań (haszowanie haseł obciąża CPU),
// - "public-lists" - publiczne listy ofert i firm (z wyszukiwaniem), osobny limit dla każdego adresu IP.
public class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";
    public const string UploadsPolicy = "uploads";
    public const string AuthPolicy = "auth";
    public const string PublicListsPolicy = "public-lists";
    public const string RejectedMessage = "Too many requests - try again later";

    public RateLimitRule Uploads { get; set; } = new() { PermitLimit = 30, WindowSeconds = 600, ConcurrencyLimit = 8 };

    public RateLimitRule Auth { get; set; } = new() { PermitLimit = 10, WindowSeconds = 60, ConcurrencyLimit = 4 };

    public WindowRateLimitRule PublicLists { get; set; } = new() { PermitLimit = 120, WindowSeconds = 60 };

    // rzuca InvalidOperationException przy złej konfiguracji (przerywa start aplikacji)
    public void Validate()
    {
        Uploads.Validate($"{SectionName}:Uploads");
        Auth.Validate($"{SectionName}:Auth");
        PublicLists.Validate($"{SectionName}:PublicLists");
    }
}

// Najwyżej PermitLimit żądań w oknie WindowSeconds na partycję (użytkownika albo adres IP); okno stałe liczone od
// pierwszego żądania partycji, Retry-After = czas do końca okna
public class WindowRateLimitRule
{
    public int PermitLimit { get; set; }
    public int WindowSeconds { get; set; }

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

// Okno jak w WindowRateLimitRule i dodatkowo najwyżej ConcurrencyLimit żądań naraz łącznie dla wszystkich partycji
// (bez kolejki - nadmiarowe dostają od razu 429)
public class RateLimitRule : WindowRateLimitRule
{
    public int ConcurrencyLimit { get; set; }

    public override void Validate(string prefix)
    {
        base.Validate(prefix);

        if (ConcurrencyLimit < 1)
        {
            throw new InvalidOperationException($"{prefix}:ConcurrencyLimit must be a positive number (got {ConcurrencyLimit}).");
        }
    }
}
