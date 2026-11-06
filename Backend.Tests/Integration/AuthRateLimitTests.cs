using System.Net;
using Backend.Controllers;
using Backend.DTOs;
using Backend.Models;
using Backend.Settings;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Tests.Integration;

// Aplikacja z domyślnymi limitami logowania (RateLimiting:Auth - 10 żądań na minutę na adres IP, najwyżej 4 naraz)
// i niskim limitem list publicznych (RateLimiting:PublicLists - domyślnie 120 na minutę, tu 5)
public class DefaultAuthRateLimitFixture : ApiFixture
{
    public const int PublicListsLimit = 5;

    public DefaultAuthRateLimitFixture()
    {
        // null - brak nadpisania z JobBoardFactory, obowiązuje wartość domyślna aplikacji
        Factory.WithSetting(JobBoardFactory.AuthPermitLimitKey, null)
            .WithSetting(JobBoardFactory.AuthConcurrencyLimitKey, null)
            .WithSetting(JobBoardFactory.PublicListsPermitLimitKey, PublicListsLimit.ToString());
    }
}

// Limity per adres klienta (brute force logowania, obciążenie CPU haszowaniem haseł, zalew list publicznych): adres
// pochodzi z X-Forwarded-For dopisanego przez nginx (UseForwardedHeaders) - każdy test używa własnych adresów
public class AuthRateLimitTests : IClassFixture<DefaultAuthRateLimitFixture>
{
    private const int DefaultAuthLimit = 10;

    private readonly DefaultAuthRateLimitFixture _api;

    public AuthRateLimitTests(DefaultAuthRateLimitFixture api) => _api = api;

    // unikalny adres klienta testu (10.x.y.z)
    private static string NewIp()
    {
        var bytes = Guid.NewGuid().ToByteArray();
        return $"10.{bytes[0]}.{bytes[1]}.{bytes[2]}";
    }

    private HttpClient ClientFrom(string forwardedFor, WebApplicationFactory<Program>? factory = null)
    {
        var client = (factory ?? _api.Factory).CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
        client.DefaultRequestHeaders.Add("X-Forwarded-For", forwardedFor);
        return client;
    }

    private static Task<HttpResponseMessage> WrongLoginAsync(HttpClient client) =>
        client.PostJsonAsync("/api/auth/login", new LoginDto { Email = $"nobody_{Guid.NewGuid():N}@test.local", Password = "Passw0rd1" });

    private static async Task AssertLimitedAsync(HttpResponseMessage response, int maxRetryAfterSeconds)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(RateLimitingSettings.RejectedMessage, await response.ReadMessageAsync());
        var retryAfter = response.Headers.RetryAfter?.Delta;
        Assert.NotNull(retryAfter);
        Assert.InRange(retryAfter.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(maxRetryAfterSeconds));
    }

    [Fact]
    public void DefaultSettings_Allow10AuthRequestsPerMinute4AtOnceAnd120ListRequestsPerMinute()
    {
        var settings = new RateLimitingSettings();

        Assert.Equal(DefaultAuthLimit, settings.Auth.PermitLimit);
        Assert.Equal(60, settings.Auth.WindowSeconds);
        Assert.Equal(4, settings.Auth.ConcurrencyLimit);
        Assert.Equal(120, settings.PublicLists.PermitLimit);
        Assert.Equal(60, settings.PublicLists.WindowSeconds);
    }

    // Każdy endpoint AuthController (także dodany później, np. reset hasła) jest objęty polityką "auth"
    [Fact]
    public void EveryAuthEndpoint_UsesTheAuthPolicy()
    {
        var endpoints = _api.Factory.Services.GetRequiredService<EndpointDataSource>().Endpoints
            .Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>()?.ControllerTypeInfo == typeof(AuthController))
            .ToList();

        Assert.Contains(endpoints, e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName == nameof(AuthController.Login));
        Assert.Contains(endpoints, e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName == nameof(AuthController.Register));
        Assert.Contains(endpoints, e => e.Metadata.GetMetadata<ControllerActionDescriptor>()!.ActionName == nameof(AuthController.RegisterEmployer));
        Assert.All(endpoints, e => Assert.Equal(RateLimitingSettings.AuthPolicy, e.Metadata.GetMetadata<EnableRateLimitingAttribute>()?.PolicyName));
    }

    [Fact]
    public async Task EleventhAuthRequestFromOneAddress_Returns429WithRetryAfter_OtherAddressesAreNotAffected()
    {
        using var client = ClientFrom(NewIp());
        // logowania i rejestracje dzielą limit adresu
        var registered = await client.PostJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = $"limit_{Guid.NewGuid():N}@test.local", Password = "Passw0rd1", FirstName = "Ra", LastName = "Te"
        });
        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        for (var i = 1; i < DefaultAuthLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await WrongLoginAsync(client)).StatusCode);
        }

        await AssertLimitedAsync(await WrongLoginAsync(client), 60);
        // limit działa przed walidacją i sprawdzeniem hasła - także poprawne dane i rejestracja dostają 429
        await AssertLimitedAsync(await client.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.UserEmail, Password = TestUsers.UserPassword }), 60);
        await AssertLimitedAsync(await client.PostJsonAsync("/api/auth/register-employer", new RegisterEmployerDto()), 60);

        using var other = ClientFrom(NewIp());
        Assert.Equal(HttpStatusCode.OK, (await other.PostJsonAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.UserEmail, Password = TestUsers.UserPassword })).StatusCode);
    }

    // nginx dopisuje adres klienta na końcu X-Forwarded-For - liczy się tylko ostatni wpis, a podrobione wcześniejsze
    // nie pozwalają obejść limitu
    [Fact]
    public async Task OnlyTheLastForwardedAddressCounts_SpoofedEntriesDoNotBypassTheLimit()
    {
        var realIp = NewIp();
        for (var i = 0; i < DefaultAuthLimit; i++)
        {
            using var spoofing = ClientFrom($"{NewIp()}, {realIp}");
            Assert.Equal(HttpStatusCode.Unauthorized, (await WrongLoginAsync(spoofing)).StatusCode);
        }

        using var sameClient = ClientFrom($"{NewIp()}, {realIp}");
        await AssertLimitedAsync(await WrongLoginAsync(sameClient), 60);

        using var direct = ClientFrom(realIp);
        await AssertLimitedAsync(await WrongLoginAsync(direct), 60);
    }

    [Fact]
    public async Task OtherEndpoints_AreNotLimitedByTheAuthPolicy()
    {
        using var client = ClientFrom(NewIp());
        for (var i = 0; i < DefaultAuthLimit; i++)
        {
            await WrongLoginAsync(client);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await WrongLoginAsync(client)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs/" + (await AnyJobIdAsync()))).StatusCode);
    }

    private async Task<int> AnyJobIdAsync() =>
        await _api.QueryDbAsync(db => Task.FromResult(db.JobOffers.Where(j => j.Status == JobOfferStatus.Active).Select(j => j.Id).First()));

    // Konto zarejestrowane z unikalnego adresu (nie zużywa limitu adresów testu)
    private async Task<string> RegisterAsync()
    {
        var email = $"ratelimit_{Guid.NewGuid():N}@test.local";
        using var client = ClientFrom(NewIp());
        var response = await client.PostJsonAsync("/api/auth/register",
            new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "Ra", LastName = "Te" });
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return email;
    }

    // Hasher, którego weryfikacja czeka na otwarcie bramki - żądanie logowania trzyma miejsce w limicie równoległości
    private sealed class GatedPasswordHasher : IPasswordHasher<ApplicationUser>
    {
        private readonly PasswordHasher<ApplicationUser> _inner = new();
        private int _waiting;

        public ManualResetEventSlim Gate { get; } = new();
        public TaskCompletionSource AllWaiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExpectedWaiting { get; init; }

        public string HashPassword(ApplicationUser user, string password) => _inner.HashPassword(user, password);

        public PasswordVerificationResult VerifyHashedPassword(ApplicationUser user, string hashedPassword, string providedPassword)
        {
            if (Interlocked.Increment(ref _waiting) == ExpectedWaiting)
            {
                AllWaiting.TrySetResult();
            }

            Gate.Wait(TimeSpan.FromSeconds(60));
            return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
        }
    }

    // Najwyżej 4 żądania uwierzytelniania naraz łącznie dla wszystkich adresów: 5. dostaje od razu 429 z Retry-After
    // (bez kolejki i bez zużycia limitu okna swojego adresu), a po zakończeniu trwających kolejne znów przechodzą
    [Fact]
    public async Task FifthConcurrentAuthRequest_Returns429UntilTheRunningOnesFinish()
    {
        const int concurrencyLimit = 4;
        var hasher = new GatedPasswordHasher { ExpectedWaiting = concurrencyLimit };
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IPasswordHasher<ApplicationUser>>();
            services.AddSingleton<IPasswordHasher<ApplicationUser>>(hasher);
        }));
        // osobne konta - logowania na to samo konto czekają na siebie (blokada per e-mail), zanim dojdą do hasła
        var emails = new List<string>();
        for (var i = 0; i < concurrencyLimit; i++)
        {
            emails.Add(await RegisterAsync());
        }

        var clients = Enumerable.Range(0, concurrencyLimit + 1).Select(_ => ClientFrom(NewIp(), factory)).ToList();
        try
        {
            var running = clients.Take(concurrencyLimit)
                .Select((c, i) => c.PostJsonAsync("/api/auth/login", new LoginDto { Email = emails[i], Password = "WrongPassw0rd1" }))
                .ToList();
            await hasher.AllWaiting.Task.WaitAsync(TimeSpan.FromSeconds(60));

            var late = clients[concurrencyLimit];
            await AssertLimitedAsync(await WrongLoginAsync(late), 1);

            hasher.Gate.Set();
            Assert.All(await Task.WhenAll(running), r => Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode));

            // odrzucenie przez limit równoległości nie zużyło limitu okna tego adresu
            for (var i = 0; i < DefaultAuthLimit; i++)
            {
                Assert.Equal(HttpStatusCode.Unauthorized, (await WrongLoginAsync(late)).StatusCode);
            }

            Assert.Equal(HttpStatusCode.TooManyRequests, (await WrongLoginAsync(late)).StatusCode);
        }
        finally
        {
            hasher.Gate.Set();
            clients.ForEach(c => c.Dispose());
        }
    }

    [Theory]
    [InlineData("/api/jobs")]
    [InlineData("/api/companies")]
    public async Task PublicList_AboveTheLimit_Returns429PerAddress(string url)
    {
        using var client = ClientFrom(NewIp());
        for (var i = 0; i < DefaultAuthRateLimitFixture.PublicListsLimit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"{url}?searchTerm=x{i}")).StatusCode);
        }

        await AssertLimitedAsync(await client.GetAsync(url), 60);

        using var other = ClientFrom(NewIp());
        Assert.Equal(HttpStatusCode.OK, (await other.GetAsync(url)).StatusCode);
    }

    [Fact]
    public async Task PublicListsShareOneLimit_DetailsAreNotLimited()
    {
        using var client = ClientFrom(NewIp());
        for (var i = 0; i < DefaultAuthRateLimitFixture.PublicListsLimit; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(i % 2 == 0 ? "/api/jobs" : "/api/companies")).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.GetAsync("/api/companies")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/jobs/" + (await AnyJobIdAsync()))).StatusCode);
    }
}

// Konfiguracja limitów logowania i list publicznych walidowana przy starcie
public class AuthRateLimitSettingsTests
{
    [Theory]
    [InlineData("RateLimiting:Auth:PermitLimit", "0")]
    [InlineData("RateLimiting:Auth:WindowSeconds", "0")]
    [InlineData("RateLimiting:Auth:ConcurrencyLimit", "-1")]
    [InlineData("RateLimiting:PublicLists:PermitLimit", "0")]
    [InlineData("RateLimiting:PublicLists:WindowSeconds", "-5")]
    public async Task Startup_WithInvalidLimit_Fails(string key, string value)
    {
        var factory = new JobBoardFactory().WithSetting(key, value);
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            var messages = new List<string>();
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                messages.Add(current.Message);
            }

            Assert.Contains(key, string.Join(" | ", messages));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }
}
