namespace Backend.Settings;

// Ustawienia JWT (sekcja "JwtSettings") - jedno źródło dla konfiguracji uwierzytelniania i TokenService
public class JwtSettings
{
    public const string SectionName = "JwtSettings";
    public const int MinSecretLength = 32;

    // TYLKO do lokalnego developmentu - używany, gdy w środowisku Development nie podano sekretu.
    // Poza Development brak sekretu kończy start aplikacji błędem.
    public const string DevelopmentSecret = "DEV-ONLY-jobboard-local-signing-key-do-not-use-in-production";

    public string Secret { get; set; } = string.Empty;
    public string Issuer { get; set; } = "JobBoardAPI";
    public string Audience { get; set; } = "JobBoardClient";
    public int ExpirationInMinutes { get; set; } = 1440;

    // true, gdy podstawiono klucz deweloperski (nie jest wczytywane z konfiguracji)
    public bool UsesDevelopmentSecret { get; private set; }

    // Uzupełnia/waliduje sekret; rzuca InvalidOperationException przy złej konfiguracji
    public void EnsureValidSecret(IHostEnvironment environment)
    {
        if (string.IsNullOrWhiteSpace(Secret))
        {
            if (!environment.IsDevelopment())
            {
                throw new InvalidOperationException(
                    $"{SectionName}:Secret is not configured. Set it via configuration or the {SectionName}__Secret environment variable.");
            }

            Secret = DevelopmentSecret;
            UsesDevelopmentSecret = true;
        }

        if (Secret.Length < MinSecretLength)
        {
            throw new InvalidOperationException(
                $"{SectionName}:Secret must be at least {MinSecretLength} characters long.");
        }
    }
}
