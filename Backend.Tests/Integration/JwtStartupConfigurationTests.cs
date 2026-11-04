using System.Net;
using System.Net.Http.Headers;
using Backend.Settings;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Start aplikacji zależy od konfiguracji JwtSettings:Secret i środowiska (JwtSettings.EnsureValidSecret).
// Każdy test używa własnej fabryki (i własnej bazy, usuwanej na końcu).
public class JwtStartupConfigurationTests
{
    private static string AllMessages(Exception exception)
    {
        var messages = new List<string>();
        for (var current = exception; current != null; current = current.InnerException!)
        {
            messages.Add(current.Message);
            if (current.InnerException == null) break;
        }

        return string.Join(" | ", messages);
    }

    private static async Task<Exception> StartupFailureAsync(string environment, string? secret)
    {
        var factory = new JobBoardFactory(environment, secret);
        try
        {
            return Assert.ThrowsAny<Exception>(() => factory.CreateClient());
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Startup_InProductionWithoutSecret_FailsWithInvalidOperationException()
    {
        var exception = await StartupFailureAsync("Production", secret: null);

        Assert.Contains("JwtSettings:Secret is not configured", AllMessages(exception));
        Assert.Contains(AllExceptions(exception), e => e is InvalidOperationException);
    }

    [Fact]
    public async Task Startup_InProductionWithWhitespaceSecret_Fails()
    {
        var exception = await StartupFailureAsync("Production", secret: "   ");

        Assert.Contains("JwtSettings:Secret is not configured", AllMessages(exception));
    }

    [Fact]
    public async Task Startup_InProductionWithTooShortSecret_Fails()
    {
        var exception = await StartupFailureAsync("Production", secret: new string('s', JwtSettings.MinSecretLength - 1));

        Assert.Contains($"at least {JwtSettings.MinSecretLength} characters", AllMessages(exception));
    }

    [Fact]
    public async Task Startup_InDevelopmentWithTooShortSecret_Fails()
    {
        var exception = await StartupFailureAsync("Development", secret: "short");

        Assert.Contains($"at least {JwtSettings.MinSecretLength} characters", AllMessages(exception));
    }

    [Fact]
    public async Task Startup_InProductionWithMinimumLengthSecret_Succeeds()
    {
        var factory = new JobBoardFactory("Production", new string('k', JwtSettings.MinSecretLength));
        try
        {
            using var client = factory.CreateClient();

            var response = await client.GetAsync("/api/jobs");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Startup_InDevelopmentWithoutSecret_UsesBuiltInDevelopmentKey()
    {
        var factory = new JobBoardFactory("Development", jwtSecret: null);
        try
        {
            using var client = factory.CreateClient();

            // token podpisany kluczem deweloperskim jest akceptowany, podpisany kluczem testowym - nie
            var devToken = JwtTestHelper.Create(JwtSettings.DevelopmentSecret);
            var otherToken = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret);

            Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "/api/admin/settings", devToken));
            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, "/api/admin/settings", otherToken));

            // a tokeny wystawiane przez login są podpisane tym samym kluczem
            var login = await client.PostJsonAsync("/api/auth/login",
                new Backend.DTOs.LoginDto { Email = TestUsers.AdminEmail, Password = TestUsers.AdminPassword });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            var issued = (await login.ReadJsonAsync<Backend.DTOs.UserDto>()).Token;
            Assert.Equal(HttpStatusCode.OK, await StatusAsync(client, "/api/admin/settings", issued));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    [Fact]
    public async Task Startup_WithConfiguredSecret_RejectsTokensSignedWithDevelopmentKey()
    {
        var factory = new JobBoardFactory("Development", JobBoardFactory.TestJwtSecret);
        try
        {
            using var client = factory.CreateClient();
            var devToken = JwtTestHelper.Create(JwtSettings.DevelopmentSecret);

            Assert.Equal(HttpStatusCode.Unauthorized, await StatusAsync(client, "/api/admin/settings", devToken));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    private static async Task<HttpStatusCode> StatusAsync(HttpClient client, string url, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return (await client.SendAsync(request)).StatusCode;
    }

    private static IEnumerable<Exception> AllExceptions(Exception exception)
    {
        for (Exception? current = exception; current != null; current = current.InnerException)
        {
            yield return current;
        }
    }
}
