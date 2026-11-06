using System.Net;
using Backend.Settings;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Aplikacja z domyślnym limitem uploadów (RateLimiting:Uploads - 30 żądań w oknie 10 minut na użytkownika)
public class DefaultUploadRateLimitFixture : ApiFixture
{
    public DefaultUploadRateLimitFixture()
    {
        // null - brak nadpisania z JobBoardFactory, obowiązuje wartość domyślna aplikacji
        Factory.WithSetting(JobBoardFactory.UploadPermitLimitKey, null);
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
    public void DefaultSettings_Allow30UploadsPer10Minutes()
    {
        var settings = new RateLimitingSettings();

        Assert.Equal(DefaultPermitLimit, settings.Uploads.PermitLimit);
        Assert.Equal(600, settings.Uploads.WindowSeconds);
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
}

// Konfiguracja limitu walidowana przy starcie
public class RateLimitingSettingsTests
{
    [Theory]
    [InlineData(0, 600, "RateLimiting:Uploads:PermitLimit")]
    [InlineData(30, 0, "RateLimiting:Uploads:WindowSeconds")]
    [InlineData(-1, 600, "RateLimiting:Uploads:PermitLimit")]
    public void Validate_RejectsNonPositiveValues(int permitLimit, int windowSeconds, string expected)
    {
        var settings = new RateLimitingSettings { Uploads = new RateLimitRule { PermitLimit = permitLimit, WindowSeconds = windowSeconds } };

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
