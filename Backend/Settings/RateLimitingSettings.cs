namespace Backend.Settings;

// Ograniczanie liczby żądań (sekcja "RateLimiting"); polityka "uploads" - upload załączników, osobny limit
// dla każdego zalogowanego użytkownika i wspólny limit równoległych uploadów
public class RateLimitingSettings
{
    public const string SectionName = "RateLimiting";
    public const string UploadsPolicy = "uploads";
    public const string RejectedMessage = "Too many requests - try again later";

    public RateLimitRule Uploads { get; set; } = new();

    // rzuca InvalidOperationException przy złej konfiguracji (przerywa start aplikacji)
    public void Validate()
    {
        Uploads.Validate($"{SectionName}:Uploads");
    }
}

// Najwyżej PermitLimit żądań w oknie WindowSeconds na użytkownika (okno stałe liczone od pierwszego żądania
// użytkownika; Retry-After = czas do końca okna) i najwyżej ConcurrencyLimit żądań naraz łącznie dla wszystkich
// użytkowników (bez kolejki - nadmiarowe dostają od razu 429)
public class RateLimitRule
{
    public int PermitLimit { get; set; } = 30;
    public int WindowSeconds { get; set; } = 600;
    public int ConcurrencyLimit { get; set; } = 8;

    public void Validate(string prefix)
    {
        if (PermitLimit < 1)
        {
            throw new InvalidOperationException($"{prefix}:PermitLimit must be a positive number (got {PermitLimit}).");
        }

        if (WindowSeconds < 1)
        {
            throw new InvalidOperationException($"{prefix}:WindowSeconds must be a positive number (got {WindowSeconds}).");
        }

        if (ConcurrencyLimit < 1)
        {
            throw new InvalidOperationException($"{prefix}:ConcurrencyLimit must be a positive number (got {ConcurrencyLimit}).");
        }
    }
}
