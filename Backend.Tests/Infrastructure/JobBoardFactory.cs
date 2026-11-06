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

    private readonly string _environment;
    private readonly Dictionary<string, string?> _settings;

    public string ConnectionString { get; }
    public string DatabaseName { get; }

    public JobBoardFactory(
        string environment = "Development",
        string? jwtSecret = TestJwtSecret,
        bool allowSeedRefresh = false,
        bool? demoAccounts = null)
    {
        _environment = environment;
        ConnectionString = TestDatabase.NewConnectionString(out var databaseName);
        DatabaseName = databaseName;

        _settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = ConnectionString,
            ["JwtSettings:Secret"] = jwtSecret ?? string.Empty,
            ["Admin:AllowSeedRefresh"] = allowSeedRefresh ? "true" : "false"
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

    // Zwalnia aplikację i usuwa bazę testową
    public async Task DisposeAndDropDatabaseAsync()
    {
        await DisposeAsync();
        await MySqlConnection.ClearAllPoolsAsync();
        await TestDatabase.DropAsync(ConnectionString);
    }
}
