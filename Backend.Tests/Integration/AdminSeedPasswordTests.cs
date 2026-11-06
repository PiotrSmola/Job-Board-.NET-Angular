using System.Net;
using Backend.Data;
using Backend.DTOs;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting.Internal;

namespace Backend.Tests.Integration;

// Hasło kont adminów z seeda: Seed:AdminPassword (stack Dockera - losowe ADMIN_PASSWORD z .env), w Development
// domyślnie Admin123!; poza Development wymagane do utworzenia kont, a istniejące konta adminów się nie zmieniają
public class AdminSeedPasswordTests
{
    private const string CustomPassword = "Str0ngAdminPassw0rd";

    private static string? Resolve(string environment, string? setting)
    {
        var values = new Dictionary<string, string?>();
        if (setting != null)
        {
            values[DatabaseSeeder.AdminPasswordKey] = setting;
        }

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(values).Build();
        return DatabaseSeeder.AdminPassword(new HostingEnvironment { EnvironmentName = environment }, configuration);
    }

    [Theory]
    [InlineData("Development", null, "Admin123!")]
    [InlineData("Development", "", "Admin123!")]
    [InlineData("Development", CustomPassword, CustomPassword)]
    [InlineData("Production", null, null)]
    [InlineData("Production", "", null)]
    [InlineData("Production", CustomPassword, CustomPassword)]
    public void AdminPassword_ComesFromTheSettingWithADevelopmentDefault(string environment, string? setting, string? expected)
    {
        Assert.Equal("Seed:AdminPassword", DatabaseSeeder.AdminPasswordKey);
        Assert.Equal(expected, Resolve(environment, setting));
    }

    private static async Task<HttpStatusCode> LoginStatusAsync(HttpClient client, string email, string password) =>
        (await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password })).StatusCode;

    [Fact]
    public async Task ConfiguredPassword_IsUsedForBothAdminAccounts()
    {
        var factory = new JobBoardFactory("Production").WithSetting(DatabaseSeeder.AdminPasswordKey, CustomPassword);
        try
        {
            using var client = factory.CreateClient();

            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(client, TestUsers.AdminEmail, CustomPassword));
            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(client, "superadmin@jobboard.com", CustomPassword));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, TestUsers.AdminEmail, TestUsers.AdminPassword));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    // Zmiana ustawienia przy istniejącej bazie nie zmienia haseł istniejących kont adminów
    [Fact]
    public async Task ExistingAdminAccounts_KeepTheirPasswordWhenTheSettingChanges()
    {
        var factory = new JobBoardFactory("Production");
        try
        {
            _ = factory.Server;
            using var restarted = factory.WithWebHostBuilder(builder =>
                builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(
                    new Dictionary<string, string?> { [DatabaseSeeder.AdminPasswordKey] = CustomPassword })));
            using var client = restarted.CreateClient();

            Assert.Equal(CustomPassword, restarted.Services.GetRequiredService<IConfiguration>()[DatabaseSeeder.AdminPasswordKey]);
            Assert.Equal(HttpStatusCode.OK, await LoginStatusAsync(client, TestUsers.AdminEmail, TestUsers.AdminPassword));
            Assert.Equal(HttpStatusCode.Unauthorized, await LoginStatusAsync(client, TestUsers.AdminEmail, CustomPassword));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    private static async Task<string> StartupErrorAsync(JobBoardFactory factory)
    {
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            var messages = new List<string>();
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                messages.Add(current.Message);
            }

            return string.Join(" | ", messages);
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    [Fact]
    public async Task ProductionWithoutThePassword_FailsToStartWithAClearMessage()
    {
        var message = await StartupErrorAsync(new JobBoardFactory("Production").WithSetting(DatabaseSeeder.AdminPasswordKey, null));

        Assert.Contains("Seed:AdminPassword is not configured", message);
    }

    [Fact]
    public async Task PasswordViolatingThePolicy_FailsToStartWithoutRevealingIt()
    {
        const string weak = "weakpass";

        var message = await StartupErrorAsync(new JobBoardFactory("Production").WithSetting(DatabaseSeeder.AdminPasswordKey, weak));

        Assert.Contains("Seed:AdminPassword", message);
        Assert.Contains("PasswordRequiresUpper", message);
        Assert.Contains("PasswordRequiresDigit", message);
        Assert.DoesNotContain(weak, message);
    }
}
