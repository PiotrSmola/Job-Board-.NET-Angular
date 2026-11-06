using System.Net;
using Backend.Interfaces;
using Backend.Services;
using Backend.Settings;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Backend.Tests.Integration;

// Aplikacja z domyślnymi limitami uploadów (RateLimiting:Uploads - 30 żądań w oknie 10 minut na użytkownika,
// najwyżej 8 uploadów naraz)
public class DefaultUploadRateLimitFixture : ApiFixture
{
    public DefaultUploadRateLimitFixture()
    {
        // null - brak nadpisania z JobBoardFactory, obowiązuje wartość domyślna aplikacji
        Factory.WithSetting(JobBoardFactory.UploadPermitLimitKey, null)
            .WithSetting(JobBoardFactory.UploadConcurrencyLimitKey, null);
    }
}

// Ograniczanie liczby żądań uploadu załączników: 31. żądanie w oknie -> 429 z komunikatem API, osobno dla każdego
// użytkownika, bez wpływu na inne endpointy
public class AttachmentRateLimitTests : IClassFixture<DefaultUploadRateLimitFixture>
{
    private const int DefaultPermitLimit = 30;

    private readonly DefaultUploadRateLimitFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentRateLimitTests(DefaultUploadRateLimitFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // Wyczerpuje limit tanimi żądaniami (formularz bez plików - 400, ale liczą się jak każde żądanie uploadu)
    private static async Task ExhaustAsync(HttpClient client, int applicationId, int alreadyUsed = 0)
    {
        for (var i = 0; i < DefaultPermitLimit - alreadyUsed; i++)
        {
            var response = await client.PostAsync(AttachmentTestFiles.Url(applicationId),
                new MultipartFormDataContent { { new StringContent("0"), "kinds" } });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }
    }

    [Fact]
    public void DefaultSettings_Allow30UploadsPer10MinutesAnd8AtOnce()
    {
        var settings = new RateLimitingSettings();

        Assert.Equal(DefaultPermitLimit, settings.Uploads.PermitLimit);
        Assert.Equal(600, settings.Uploads.WindowSeconds);
        Assert.Equal(8, settings.Uploads.ConcurrencyLimit);
    }

    [Fact]
    public async Task ThirtyFirstUploadInTheWindow_Returns429WithMessageAndStoresNothing()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);
        Assert.Equal(HttpStatusCode.Created, (await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
        for (var i = 1; i < DefaultPermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id),
                new MultipartFormDataContent())).StatusCode);
        }

        var limited = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf(1), "late.pdf");

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(RateLimitingSettings.RejectedMessage, await limited.ReadMessageAsync());
        Assert.True(limited.Headers.RetryAfter?.Delta > TimeSpan.Zero, "Retry-After header expected");
        Assert.Single(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Single(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task LimitIsPerUser_AndDoesNotAffectOtherEndpoints()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        var (otherCandidate, otherApplication) = await _attachments.ApplyAnotherAsync(setup);
        using var client = _api.CreateClient(setup.Candidate);
        // jeden upload już wykonany przez tego kandydata (UploadOneAsync)
        await ExhaustAsync(client, setup.Application.Id, alreadyUsed: 1);
        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);

        // inny kandydat nadal wgrywa
        using var other = _api.CreateClient(otherCandidate);
        Assert.Equal(HttpStatusCode.Created, (await other.UploadAttachmentAsync(otherApplication.Id, AttachmentTestFiles.Pdf())).StatusCode);

        // pobieranie, usuwanie i odczyt aplikacji nie są objęte limitem uploadów
        Assert.Equal(HttpStatusCode.OK, (await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/jobapplications/{setup.Application.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAttachmentAsync(setup.Application.Id, attachment.Id)).StatusCode);
    }

    [Fact]
    public async Task RequestsOnStrangersApplications_CountTowardsTheLimitToo()
    {
        var setup = await _attachments.NewSetupAsync();
        var stranger = await _api.RegisterUserAsync("flood");
        using var client = _api.CreateClient(stranger);

        for (var i = 0; i < DefaultPermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.NotFound, (await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
        }

        Assert.Equal(HttpStatusCode.TooManyRequests, (await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
    }

    [Fact]
    public async Task AnonymousRequests_Get401NotTheLimit()
    {
        var setup = await _attachments.NewSetupAsync();
        using var anonymous = _api.CreateClient();

        for (var i = 0; i <= DefaultPermitLimit; i++)
        {
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
        }
    }

    // Retry-After to czas do końca okna (liczonego od pierwszego żądania), nie zawsze pełne 10 minut
    [Fact]
    public async Task RetryAfter_CountsDownToTheEndOfTheWindow()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);
        await ExhaustAsync(client, setup.Application.Id);

        var first = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf());
        await Task.Delay(TimeSpan.FromSeconds(1.5));
        var second = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.TooManyRequests, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        var firstRetry = first.Headers.RetryAfter?.Delta;
        var secondRetry = second.Headers.RetryAfter?.Delta;
        Assert.NotNull(firstRetry);
        Assert.NotNull(secondRetry);
        Assert.InRange(firstRetry.Value, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(600));
        Assert.True(secondRetry < firstRetry, $"Retry-After should decrease: {firstRetry} -> {secondRetry}");
    }

    // Magazyn, którego zapis czeka na otwarcie bramki - upload trzyma miejsce w limicie równoległości
    private sealed class GatedFileStorage(IFileStorage inner) : IFileStorage
    {
        private int _waiting;

        public TaskCompletionSource Gate { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource AllWaiting { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int ExpectedWaiting { get; init; }

        public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref _waiting) == ExpectedWaiting)
            {
                AllWaiting.TrySetResult();
            }

            await Gate.Task.WaitAsync(TimeSpan.FromSeconds(60), cancellationToken);
            await inner.SaveAsync(key, content, contentType, cancellationToken);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => inner.InitializeAsync(cancellationToken);
        public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default) => inner.OpenReadAsync(key, cancellationToken);
        public Task DeleteAsync(string key, CancellationToken cancellationToken = default) => inner.DeleteAsync(key, cancellationToken);
        public Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default) => inner.DeleteByPrefixAsync(prefix, cancellationToken);
    }

    // Najwyżej 8 uploadów naraz łącznie dla wszystkich użytkowników: 9. dostaje od razu 429 (bez kolejki i bez
    // zużycia limitu okna), a po zakończeniu trwających uploadów kolejne znów przechodzą
    [Fact]
    public async Task NinthConcurrentUpload_Returns429UntilTheRunningOnesFinish()
    {
        const int concurrencyLimit = 8;
        var storage = new GatedFileStorage(new LocalFileStorage(_api.Factory.StoragePath)) { ExpectedWaiting = concurrencyLimit };
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<IFileStorage>();
            services.AddSingleton<IFileStorage>(storage);
        }));
        var setups = new List<AttachmentSetup>();
        for (var i = 0; i < concurrencyLimit + 1; i++)
        {
            setups.Add(await _attachments.NewSetupAsync());
        }

        HttpClient ClientFor(TestUser user)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);
            return client;
        }

        var clients = setups.Select(s => ClientFor(s.Candidate)).ToList();
        try
        {
            // 8 użytkowników - każdy jeden upload, wszystkie czekają w zapisie pliku
            var running = setups.Take(concurrencyLimit)
                .Select((s, i) => clients[i].UploadAttachmentAsync(s.Application.Id, AttachmentTestFiles.Pdf(i)))
                .ToList();
            await storage.AllWaiting.Task.WaitAsync(TimeSpan.FromSeconds(60));

            var late = setups[concurrencyLimit];
            var rejected = await clients[concurrencyLimit].UploadAttachmentAsync(late.Application.Id, AttachmentTestFiles.Pdf());

            Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);
            Assert.Equal(RateLimitingSettings.RejectedMessage, await rejected.ReadMessageAsync());

            storage.Gate.SetResult();
            Assert.All(await Task.WhenAll(running), r => Assert.Equal(HttpStatusCode.Created, r.StatusCode));

            // miejsca zwolnione - ten sam użytkownik wgrywa (odrzucenie nie zużyło jego limitu okna)
            Assert.Equal(HttpStatusCode.Created,
                (await clients[concurrencyLimit].UploadAttachmentAsync(late.Application.Id, AttachmentTestFiles.Pdf())).StatusCode);
            await ExhaustAsync(clients[concurrencyLimit], late.Application.Id, alreadyUsed: 1);
        }
        finally
        {
            storage.Gate.TrySetResult();
            clients.ForEach(c => c.Dispose());
        }
    }
}

// Konfiguracja limitu walidowana przy starcie
public class RateLimitingSettingsTests
{
    [Theory]
    [InlineData(0, 600, "RateLimiting:Uploads:PermitLimit", 8)]
    [InlineData(30, 0, "RateLimiting:Uploads:WindowSeconds", 8)]
    [InlineData(-1, 600, "RateLimiting:Uploads:PermitLimit", 8)]
    [InlineData(30, 600, "RateLimiting:Uploads:ConcurrencyLimit", 0)]
    public void Validate_RejectsNonPositiveValues(int permitLimit, int windowSeconds, string expected, int concurrencyLimit)
    {
        var settings = new RateLimitingSettings
        {
            Uploads = new RateLimitRule { PermitLimit = permitLimit, WindowSeconds = windowSeconds, ConcurrencyLimit = concurrencyLimit }
        };

        var exception = Assert.Throws<InvalidOperationException>(settings.Validate);

        Assert.Contains(expected, exception.Message);
    }

    [Fact]
    public async Task Startup_WithInvalidRateLimit_Fails()
    {
        var factory = new JobBoardFactory().WithSetting(JobBoardFactory.UploadPermitLimitKey, "0");
        try
        {
            var exception = Assert.ThrowsAny<Exception>(() => factory.CreateClient());
            var messages = new List<string>();
            for (Exception? current = exception; current != null; current = current.InnerException)
            {
                messages.Add(current.Message);
            }

            Assert.Contains("RateLimiting:Uploads:PermitLimit", string.Join(" | ", messages));
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }
}
