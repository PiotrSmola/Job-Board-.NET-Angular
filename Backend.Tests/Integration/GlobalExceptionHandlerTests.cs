using System.Collections.Concurrent;
using System.Net;
using Backend.Services;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Backend.Tests.Integration;

// Endpoint testowy dodawany tylko w tych testach (AddApplicationPart) - nieobsłużony wyjątek z tajną treścią
[ApiController]
[AllowAnonymous]
[Route("api/test-exceptions")]
public class ExceptionTestController : ControllerBase
{
    public const string Secret = "connection-string-password=hunter2";

    [HttpGet("throw")]
    public IActionResult Throw() => throw new InvalidOperationException(Secret);

    // błąd klienta przy odczycie żądania (jak z Kestrela albo strumienia z limitem) z danym kodem statusu
    [HttpGet("bad-request/{status:int}")]
    public IActionResult ThrowBadRequest(int status) => throw new BadHttpRequestException(Secret, status);
}

// Globalna obsługa wyjątków (UseExceptionHandler + GlobalExceptionHandler): nieobsłużony wyjątek -> 500
// {message:"An unexpected error occurred"} bez szczegółów, w Production i w Development, z wpisem w logu
public class GlobalExceptionHandlerTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<(LogLevel Level, string Category, Exception? Exception)> Entries { get; } = new();

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(this, categoryName);

        public void Dispose()
        {
        }

        private sealed class CapturingLogger(CapturingLoggerProvider provider, string category) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
                Func<TState, Exception?, string> formatter) =>
                provider.Entries.Enqueue((logLevel, category, exception));
        }
    }

    private static async Task RunAsync(string environment, Func<HttpClient, CapturingLoggerProvider, Task> test)
    {
        var logs = new CapturingLoggerProvider();
        var factory = new JobBoardFactory(environment);
        try
        {
            using var withController = factory.WithWebHostBuilder(builder =>
            {
                builder.ConfigureLogging(logging => logging.AddProvider(logs));
                builder.ConfigureTestServices(services =>
                    services.AddControllers().AddApplicationPart(typeof(ExceptionTestController).Assembly));
            });
            using var client = withController.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            await test(client, logs);
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    public async Task UnhandledException_Returns500WithGenericMessageAndLogsTheException(string environment)
    {
        await RunAsync(environment, async (client, logs) =>
        {
            var response = await client.GetAsync("/api/test-exceptions/throw");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            var body = await response.Content.ReadAsStringAsync();
            Assert.Equal(GlobalExceptionHandler.Message, await response.ReadMessageAsync());
            Assert.DoesNotContain(ExceptionTestController.Secret, body);
            Assert.DoesNotContain("InvalidOperationException", body);
            Assert.DoesNotContain("at Backend", body);

            // wyjątek ze stosem trafia do logu (poziom Error)
            Assert.Contains(logs.Entries, e => e.Level == LogLevel.Error && e.Exception?.Message == ExceptionTestController.Secret);
        });
    }

    [Fact]
    public async Task OrdinaryErrorResponses_AreNotTouchedByTheHandler()
    {
        await RunAsync("Production", async (client, _) =>
        {
            var notFound = await client.GetAsync("/api/jobs/987654321");
            Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
            Assert.Equal("Job offer not found", await notFound.ReadMessageAsync());

            Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/jobapplications/my-applications")).StatusCode);
        });
    }

    // BadHttpRequestException (za duże ciało, przekroczony czas, uszkodzone żądanie) to błąd klienta: jego kod statusu,
    // ogólny komunikat bez treści wyjątku i log Warning zamiast Error
    [Theory]
    [InlineData(413, GlobalExceptionHandler.RequestTooLargeMessage)]
    [InlineData(408, GlobalExceptionHandler.RequestTimeoutMessage)]
    [InlineData(400, GlobalExceptionHandler.BadRequestMessage)]
    [InlineData(431, GlobalExceptionHandler.BadRequestMessage)]
    public async Task BadHttpRequestException_ReturnsItsStatusCodeAndIsNotLoggedAsError(int status, string message)
    {
        await RunAsync("Production", async (client, logs) =>
        {
            var response = await client.GetAsync($"/api/test-exceptions/bad-request/{status}");

            Assert.Equal(status, (int)response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(message, await response.ReadMessageAsync());
            Assert.DoesNotContain(ExceptionTestController.Secret, await response.Content.ReadAsStringAsync());

            Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
            Assert.Contains(logs.Entries, e => e.Level == LogLevel.Warning && e.Category == typeof(ClientRequestErrorMiddleware).FullName);
        });
    }

    [Fact]
    public async Task BadHttpRequestExceptionWithAServerErrorStatus_IsStillAClientError()
    {
        await RunAsync("Production", async (client, logs) =>
        {
            var response = await client.GetAsync("/api/test-exceptions/bad-request/500");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(GlobalExceptionHandler.BadRequestMessage, await response.ReadMessageAsync());
            Assert.DoesNotContain(logs.Entries, e => e.Level >= LogLevel.Error);
        });
    }

    // BadHttpRequestException spoza ClientRequestErrorMiddleware (np. z middleware'u przed nim) - handler też
    // zwraca kod statusu klienta, nie 500
    [Fact]
    public async Task Handler_MapsBadHttpRequestExceptionToItsStatusCode()
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };

        var handled = await new GlobalExceptionHandler().TryHandleAsync(context,
            new BadHttpRequestException(ExceptionTestController.Secret, StatusCodes.Status413PayloadTooLarge), CancellationToken.None);

        Assert.True(handled);
        Assert.Equal(StatusCodes.Status413PayloadTooLarge, context.Response.StatusCode);
        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        Assert.Contains(GlobalExceptionHandler.RequestTooLargeMessage, body);
        Assert.DoesNotContain(ExceptionTestController.Secret, body);
    }
}
