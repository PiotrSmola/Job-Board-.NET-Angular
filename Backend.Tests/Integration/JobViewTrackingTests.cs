using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Services;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Backend.Tests.Integration;

// Zliczanie wyświetleń GET /api/jobs/{id}: jedno na (oferta, odwiedzający, dzień UTC), bez zliczania dla zarządzających
// ofertą, błąd zliczania nie psuje odpowiedzi, a wpisy deduplikacji są sprzątane.
public class JobViewTrackingTests : IClassFixture<StatsFixture>
{
    private readonly StatsFixture _api;

    public JobViewTrackingTests(StatsFixture api) => _api = api;

    private async Task<TestEmployer> NewCompanyWithJobAsync(Action<CreateJobOfferDto>? customize = null)
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        _jobs[employer.CompanyId] = await _api.CreateEmployerJobAsync(employer, customize);
        return employer;
    }

    private readonly Dictionary<int, JobOfferDto> _jobs = new();

    private static async Task<HttpStatusCode> GetJobAsync(HttpClient client, int jobId, string? userAgent = null)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/jobs/{jobId}");
        if (userAgent != null)
        {
            request.Headers.UserAgent.ParseAdd(userAgent);
        }

        return (await client.SendAsync(request)).StatusCode;
    }

    private Task<int> ViewsAsync(int jobId) =>
        _api.QueryDbAsync(db => db.DailyViews.Where(v => v.JobOfferId == jobId).SumAsync(v => v.Views));

    private Task<int> ViewersAsync(int jobId) =>
        _api.QueryDbAsync(db => db.JobOfferViewers.CountAsync(v => v.JobOfferId == jobId));

    [Fact]
    public async Task AnonymousVisitor_IsCountedOncePerDay_AndDifferentBrowsersSeparately()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, await GetJobAsync(anonymous, jobId, "Browser-A/1.0"));
        Assert.Equal(HttpStatusCode.OK, await GetJobAsync(anonymous, jobId, "Browser-A/1.0"));
        Assert.Equal(1, await ViewsAsync(jobId));

        await GetJobAsync(anonymous, jobId, "Browser-B/2.0");
        Assert.Equal(2, await ViewsAsync(jobId));
        Assert.Equal(2, await ViewersAsync(jobId));

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var row = await _api.QueryDbAsync(db => db.DailyViews.SingleAsync(v => v.JobOfferId == jobId));
        Assert.Contains(row.Date, new[] { today, today.AddDays(-1) });
    }

    // za nginx odwiedzający to adres z X-Forwarded-For (ostatni wpis) - ten sam co w limitach żądań per IP
    [Fact]
    public async Task AnonymousVisitorsBehindTheProxy_AreCountedByTheForwardedAddress()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        using var first = _api.CreateClient();
        first.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.10");
        using var spoofed = _api.CreateClient();
        spoofed.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.99, 203.0.113.10");
        using var second = _api.CreateClient();
        second.DefaultRequestHeaders.Add("X-Forwarded-For", "203.0.113.11");

        await GetJobAsync(first, jobId, "Browser-A/1.0");
        await GetJobAsync(spoofed, jobId, "Browser-A/1.0");
        Assert.Equal(1, await ViewsAsync(jobId));

        await GetJobAsync(second, jobId, "Browser-A/1.0");
        Assert.Equal(2, await ViewsAsync(jobId));
    }

    [Fact]
    public async Task SignedInUsers_AreCountedOnceEach_SeparatelyFromAnonymousVisitors()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        var first = await _api.RegisterUserAsync();
        var second = await _api.RegisterUserAsync();
        using var firstClient = _api.CreateClient(first);
        using var secondClient = _api.CreateClient(second);
        using var anonymous = _api.CreateClient();

        await GetJobAsync(firstClient, jobId, "Same/1.0");
        await GetJobAsync(firstClient, jobId, "Other/9.9"); // ten sam użytkownik, inna przeglądarka
        await GetJobAsync(secondClient, jobId, "Same/1.0");
        await GetJobAsync(anonymous, jobId, "Same/1.0");

        Assert.Equal(3, await ViewsAsync(jobId));
    }

    [Fact]
    public async Task ManagersOfTheOffer_AreNotCounted_ButOtherEmployersAre()
    {
        var owner = await NewCompanyWithJobAsync();
        var jobId = _jobs[owner.CompanyId].Id;
        var stranger = await _api.RegisterApprovedEmployerAsync();

        using (var ownerClient = _api.CreateClient(owner))
        using (var admin = await _api.AdminClientAsync())
        {
            Assert.Equal(HttpStatusCode.OK, await GetJobAsync(ownerClient, jobId));
            Assert.Equal(HttpStatusCode.OK, await GetJobAsync(admin, jobId));
        }

        Assert.Equal(0, await ViewsAsync(jobId));
        Assert.Equal(0, await ViewersAsync(jobId));

        using var strangerClient = _api.CreateClient(stranger);
        Assert.Equal(HttpStatusCode.OK, await GetJobAsync(strangerClient, jobId));
        Assert.Equal(1, await ViewsAsync(jobId));
    }

    [Fact]
    public async Task HiddenAndMissingOffers_AreNotCounted()
    {
        var owner = await NewCompanyWithJobAsync(j => j.Status = JobOfferStatus.Draft);
        var draftId = _jobs[owner.CompanyId].Id;
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, await GetJobAsync(anonymous, draftId));
        Assert.Equal(HttpStatusCode.NotFound, await GetJobAsync(anonymous, 987654321));

        Assert.Equal(0, await ViewsAsync(draftId));
        Assert.Equal(0, await ViewersAsync(draftId));
    }

    [Fact]
    public async Task ParallelRequestsOfOneVisitor_AreCountedOnce()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        using var anonymous = _api.CreateClient();

        var results = await Task.WhenAll(Enumerable.Range(0, 12).Select(_ => GetJobAsync(anonymous, jobId, "Parallel/1.0")));

        Assert.All(results, status => Assert.Equal(HttpStatusCode.OK, status));
        Assert.Equal(1, await ViewsAsync(jobId));
        Assert.Equal(1, await ViewersAsync(jobId));
    }

    [Fact]
    public async Task Visitor_IsStoredAsHashOnly_NotAsIdentifierNorAddress()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        var user = await _api.RegisterUserAsync();
        using var client = _api.CreateClient(user);

        await GetJobAsync(client, jobId);

        var hash = (await _api.QueryDbAsync(db => db.JobOfferViewers.SingleAsync(v => v.JobOfferId == jobId))).VisitorHash;
        Assert.Equal(16, hash.Length);
        var asText = System.Text.Encoding.UTF8.GetString(hash);
        Assert.DoesNotContain(user.Id, asText);
        // tabela nie ma żadnej kolumny na adres, identyfikator użytkownika ani User-Agent
        var columns = await _api.QueryDbAsync(async db =>
            await db.Database.SqlQuery<string>(
                $"SELECT COLUMN_NAME AS Value FROM information_schema.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = 'JobOfferViewers'")
                .ToListAsync());
        Assert.Equal(new[] { "Date", "JobOfferId", "VisitorHash" }, columns.Order().ToArray());
    }

    [Fact]
    public async Task CountingFailure_DoesNotBreakTheOfferResponse()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        using var anonymous = _api.CreateClient();

        await _api.QueryDbAsync(async db => await db.Database.ExecuteSqlRawAsync("RENAME TABLE `JobOfferViewers` TO `JobOfferViewers_off`"));
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, $"/api/jobs/{jobId}");
            var response = await anonymous.SendAsync(request);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(jobId, (await response.ReadJsonAsync<JobOfferDto>()).Id);
        }
        finally
        {
            await _api.QueryDbAsync(async db => await db.Database.ExecuteSqlRawAsync("RENAME TABLE `JobOfferViewers_off` TO `JobOfferViewers`"));
        }

        Assert.Equal(0, await ViewsAsync(jobId));
    }

    [Fact]
    public async Task Cleanup_RemovesDeduplicationRecordsOlderThanYesterday_AndKeepsTheAggregates()
    {
        var employer = await NewCompanyWithJobAsync();
        var jobId = _jobs[employer.CompanyId].Id;
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        await _api.QueryDbAsync(async db =>
        {
            db.JobOfferViewers.AddRange(
                Viewer(jobId, today.AddDays(-30), 1), Viewer(jobId, today.AddDays(-2), 2),
                Viewer(jobId, today.AddDays(-1), 3), Viewer(jobId, today, 4));
            db.DailyViews.Add(new JobOfferDailyViews { JobOfferId = jobId, Date = today.AddDays(-30), Views = 9 });
            await db.SaveChangesAsync();
            return 0;
        });

        var cleanup = _api.Factory.Services.GetServices<IHostedService>().OfType<JobViewCleanupService>().Single();
        var removed = await cleanup.CleanUpAsync(CancellationToken.None);

        Assert.True(removed >= 2);
        var dates = await _api.QueryDbAsync(db => db.JobOfferViewers.Where(v => v.JobOfferId == jobId).Select(v => v.Date).ToListAsync());
        Assert.Equal(new[] { today.AddDays(-1), today }, dates.Order().ToArray());
        Assert.Equal(9, await ViewsAsync(jobId));
    }

    private static JobOfferViewer Viewer(int jobId, DateOnly date, byte seed) =>
        new() { JobOfferId = jobId, Date = date, VisitorHash = Enumerable.Repeat(seed, 16).ToArray() };
}
