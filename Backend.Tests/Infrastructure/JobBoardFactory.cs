using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using MySqlConnector;

namespace Backend.Tests.Infrastructure;

// Fabryka aplikacji dla testów integracyjnych - własna baza MySQL (migracje + seed przy starcie aplikacji)
public class JobBoardFactory : WebApplicationFactory<Program>
{
    // sekret >= 32 znaki, niezależny od klucza deweloperskiego z JwtSettings.DevelopmentSecret
    public const string TestJwtSecret = "integration-tests-signing-key-0123456789-abcdef";

    public const string UploadPermitLimitKey = "RateLimiting:Uploads:PermitLimit";
    public const string UploadConcurrencyLimitKey = "RateLimiting:Uploads:ConcurrencyLimit";
    public const string AuthPermitLimitKey = "RateLimiting:Auth:PermitLimit";
    public const string AuthConcurrencyLimitKey = "RateLimiting:Auth:ConcurrencyLimit";
    public const string PublicListsPermitLimitKey = "RateLimiting:PublicLists:PermitLimit";

    private readonly string _environment;
    private readonly Dictionary<string, string?> _settings;

    public string ConnectionString { get; }
    public string DatabaseName { get; }

    // katalog plików (Storage:Provider = Local) - osobny dla każdej fabryki, usuwany razem z bazą
    public string StoragePath { get; }

    public JobBoardFactory(
        string environment = "Development",
        string? jwtSecret = TestJwtSecret,
        bool allowSeedRefresh = false,
        bool? demoAccounts = null)
    {
        _environment = environment;
        ConnectionString = TestDatabase.NewConnectionString(out var databaseName);
        DatabaseName = databaseName;
        StoragePath = Path.Combine(Path.GetTempPath(), "jobboard-tests", databaseName);

        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["JwtSettings:Secret"] = jwtSecret ?? string.Empty,
            ["Admin:AllowSeedRefresh"] = allowSeedRefresh ? "true" : "false",
            ["Storage:Provider"] = "Local",
            ["Storage:LocalPath"] = StoragePath,
            // testy wgrywają wiele plików jako ten sam użytkownik, także równolegle - limity uploadów podniesione;
            // testy limitów ustawiają własne wartości (WithSetting) albo przywracają domyślne (null)
            [UploadPermitLimitKey] = "100000",
            [UploadConcurrencyLimitKey] = "1000",
            // wszystkie żądania testów mają ten sam adres klienta (TestServer) i logują się wielokrotnie, także równolegle -
            // limity logowania i list publicznych podniesione; testy tych limitów ustawiają własne wartości
            [AuthPermitLimitKey] = "100000",
            [AuthConcurrencyLimitKey] = "1000",
            [PublicListsPermitLimitKey] = "100000"
        };

        // brak wartości = domyślne zachowanie seedera (konta demo tylko w Development)
        if (demoAccounts.HasValue)
        {
            _settings["Seed:DemoAccounts"] = demoAccounts.Value ? "true" : "false";
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_environment);

        // źródło dodane jako ostatnie ma najwyższy priorytet - wygrywa ze zmienną ConnectionStrings__DefaultConnection
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(_settings));

        builder.ConfigureLogging(logging => logging.ClearProviders());
    }

    // Nadpisanie ustawień przed startem aplikacji (np. magazyn S3 albo zła konfiguracja magazynu)
    public JobBoardFactory WithSetting(string key, string? value)
    {
        _settings[key] = value;
        return this;
    }

    // Zwalnia aplikację, usuwa bazę testową i katalog plików
    public async Task DisposeAndDropDatabaseAsync()
    {
        await DisposeAsync();
        await MySqlConnection.ClearAllPoolsAsync();
        await TestDatabase.DropAsync(ConnectionString);

        if (Directory.Exists(StoragePath))
        {
            Directory.Delete(StoragePath, recursive: true);
        }
    }
}
