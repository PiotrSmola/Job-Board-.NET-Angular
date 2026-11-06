using System.Net;
using System.Text;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// GET /api/admin/stats i /api/employer/stats (+ eksport CSV) na kontrolowanych danych z przeszłego zakresu
// 2024-05-01..2024-05-10 (patrz StatsFixture). Dni, wyświetlenia, aplikacje i historia mają dokładnie znane wartości.
public class StatsTests : IClassFixture<StatsFixture>
{
    private const string Range = StatsFixture.RangeQuery;
    private const string NoCompanyMessage = "Employer account is not linked to a company";

    private readonly StatsFixture _api;

    public StatsTests(StatsFixture api) => _api = api;

    private async Task<EmployerStatsDto> EmployerStatsAsync(TestEmployer employer, string query = Range)
    {
        using var client = _api.CreateClient(employer);
        var response = await client.GetAsync($"/api/employer/stats?{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<EmployerStatsDto>();
    }

    private static DateOnly Day(string day) => DateOnly.ParseExact(day, "yyyy-MM-dd");

    // ---------- metryki pracodawcy ----------

    [Fact]
    public async Task Employer_RangeAndSeries_CoverEveryDayWithZeroFill()
    {
        var s = await _api.ScenarioAsync();

        var stats = await EmployerStatsAsync(s.Employer);

        Assert.Equal(new StatsRangeDto(Day("2024-05-01"), Day("2024-05-10")), stats.Range);
        Assert.Equal(10, stats.Series.Count);
        Assert.Equal(Enumerable.Range(1, 10).Select(d => Day($"2024-05-{d:00}")), stats.Series.Select(p => p.Date));
        // 05-02: app1 i app2 (23:59:59 nadal 2 maja); 05-04: app3, app4; aplikacje z 30.04 i 11.05 poza zakresem
        Assert.Equal(new[] { 0, 2, 0, 2, 0, 0, 0, 0, 0, 0 }, stats.Series.Select(p => p.NewApplications));
        // wyświetlenia: oferta A 5 + 3, oferta B 2 (wpis z 30.04 poza zakresem)
        Assert.Equal(new[] { 0, 5, 0, 5, 0, 0, 0, 0, 0, 0 }, stats.Series.Select(p => p.JobViews));
    }

    [Fact]
    public async Task Employer_Totals_CountOfferStatesApplicationsByStatusAndAllTimeViews()
    {
        var s = await _api.ScenarioAsync();

        var totals = (await EmployerStatsAsync(s.Employer)).Totals;

        Assert.Equal(new JobStatusCountsDto(Draft: 1, Active: 1, Expired: 1, Closed: 0), totals.Jobs);
        // 4 aplikacje w zakresie + 2 poza zakresem; łączne wyświetlenia obejmują też wpis z 30.04 (7)
        Assert.Equal(6, totals.Applications.Total);
        Assert.Equal(7, totals.Applications.ByStatus.Count);
        // Submitted 2, InReview 0, Interview 1, Offered 0, Hired 1, Rejected 1, Withdrawn 1
        Assert.Equal(
            new[] { 2, 0, 1, 0, 1, 1, 1 },
            Enum.GetValues<ApplicationStatus>().Select(st => totals.Applications.ByStatus.Single(b => b.Status == st).Count));
        Assert.Equal(17, totals.Views);
    }

    [Fact]
    public async Task Employer_Funnel_CountsReachedStagesCumulatively()
    {
        var s = await _api.ScenarioAsync();

        var funnel = (await EmployerStatsAsync(s.Employer)).Funnel;

        // app1 -> Interview, app2 odrzucona, app3 wycofana, app4 -> Hired (bez etapów pośrednich w historii)
        Assert.Equal(new StatsFunnelDto(Submitted: 4, InReview: 2, Interview: 2, Offered: 1, Hired: 1), funnel);
    }

    [Fact]
    public async Task Employer_MedianFirstResponse_IgnoresCandidateActionsAndAppsWithoutResponse()
    {
        var s = await _api.ScenarioAsync();

        var stats = await EmployerStatsAsync(s.Employer);

        // odpowiedzi rekrutera po 2 h, 4 h i 10 h; wycofanie przez kandydata i aplikacja bez odpowiedzi nie liczą się
        Assert.Equal(4.0, stats.MedianFirstResponseHours);
    }

    [Fact]
    public async Task Employer_Jobs_ListsAllOffersWithRangeCountsAndConversion()
    {
        var s = await _api.ScenarioAsync();

        var jobs = (await EmployerStatsAsync(s.Employer)).Jobs;

        Assert.Equal(new[] { s.JobA.Id, s.JobB.Id, s.Draft.Id }, jobs.Select(j => j.JobId));

        var a = jobs[0];
        Assert.Equal((JobOfferStatus.Active, false, 8, 3, 0.375), (a.Status, a.IsExpired, a.Views, a.Applications, a.ConversionRate));
        Assert.Equal(s.JobA.Title, a.Title);

        var b = jobs[1];
        Assert.Equal((JobOfferStatus.Active, true, 2, 1, 0.5), (b.Status, b.IsExpired, b.Views, b.Applications, b.ConversionRate));

        var draft = jobs[2];
        Assert.Equal((JobOfferStatus.Draft, false, 0, 0, 0.0), (draft.Status, draft.IsExpired, draft.Views, draft.Applications, draft.ConversionRate));
    }

    [Fact]
    public async Task Employer_SeesNothingOfAnotherCompany()
    {
        var s = await _api.ScenarioAsync();

        var stats = await EmployerStatsAsync(s.Other);

        Assert.Equal(new[] { s.JobD.Id }, stats.Jobs.Select(j => j.JobId));
        Assert.Equal(1, stats.Totals.Applications.Total);
        Assert.Equal(4, stats.Totals.Views);
        Assert.Equal(new StatsFunnelDto(1, 1, 1, 1, 0), stats.Funnel);
        Assert.Equal(1.0, stats.MedianFirstResponseHours);
    }

    [Fact]
    public async Task Employer_WithoutActivity_GetsZerosAndNoMedian()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();

        var stats = await EmployerStatsAsync(employer);

        Assert.Equal(10, stats.Series.Count);
        Assert.All(stats.Series, p => Assert.Equal((0, 0), (p.NewApplications, p.JobViews)));
        Assert.Equal(new StatsFunnelDto(0, 0, 0, 0, 0), stats.Funnel);
        Assert.Empty(stats.Jobs);
        Assert.Equal(0, stats.Totals.Applications.Total);
        Assert.Null(stats.MedianFirstResponseHours);
    }

    [Fact]
    public async Task Employer_WithPendingCompany_StillGetsHisData()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);

        var stats = await EmployerStatsAsync(employer);

        Assert.Equal(1, stats.Totals.Jobs.Draft);
        Assert.Single(stats.Jobs);
    }

    // ---------- metryki admina ----------

    [Fact]
    public async Task Admin_Series_AggregatesAllCompanies()
    {
        var s = await _api.ScenarioAsync();

        var series = s.AdminAfter.Series;

        Assert.Equal(10, series.Count);
        Assert.Equal(new[] { 0, 2, 0, 3, 0, 0, 0, 0, 0, 0 }, series.Select(p => p.NewApplications));
        // oferty: A i szkic 2 maja (B z 20.04 poza zakresem), D 4 maja
        Assert.Equal(new[] { 0, 2, 0, 1, 0, 0, 0, 0, 0, 0 }, series.Select(p => p.NewJobs));
        Assert.Equal(new[] { 4, 0, 2, 0, 0, 0, 0, 0, 0, 0 }, series.Select(p => p.NewUsers));
        Assert.Equal(new[] { 0, 5, 0, 9, 0, 0, 0, 0, 0, 0 }, series.Select(p => p.JobViews));
    }

    [Fact]
    public async Task Admin_FunnelMedianAndTopJobs_AreComputedForTheRange()
    {
        var s = await _api.ScenarioAsync();
        var stats = s.AdminAfter;

        Assert.Equal(new StatsFunnelDto(Submitted: 5, InReview: 3, Interview: 3, Offered: 2, Hired: 1), stats.Funnel);
        // odpowiedzi po 1 h, 2 h, 4 h i 10 h -> mediana (2 + 4) / 2
        Assert.Equal(3.0, stats.MedianFirstResponseHours);

        Assert.Equal(new[] { s.JobA.Id, s.JobD.Id, s.JobB.Id }, stats.TopJobs.Select(j => j.JobId));
        Assert.Equal(new AdminTopJobDto(s.JobA.Id, s.JobA.Title, s.Employer.CompanyName, 3, 8), stats.TopJobs[0]);
        Assert.Equal(new AdminTopJobDto(s.JobD.Id, s.JobD.Title, s.Other.CompanyName, 1, 4), stats.TopJobs[1]);
        Assert.Equal(new AdminTopJobDto(s.JobB.Id, s.JobB.Title, s.Employer.CompanyName, 1, 2), stats.TopJobs[2]);
    }

    [Fact]
    public async Task Admin_Totals_GrowByTheSeededData()
    {
        var s = await _api.ScenarioAsync();
        var before = s.AdminBefore.Totals;
        var after = s.AdminAfter.Totals;

        // 6 kandydatów i 2 pracodawców z zatwierdzonymi firmami
        Assert.Equal(before.Users.Candidates + 6, after.Users.Candidates);
        Assert.Equal(before.Users.Employers + 2, after.Users.Employers);
        Assert.Equal(before.Users.Admins, after.Users.Admins);
        Assert.Equal(before.Companies.Approved + 2, after.Companies.Approved);
        Assert.Equal(before.Companies.Pending, after.Companies.Pending);
        Assert.Equal(before.Jobs.Draft + 1, after.Jobs.Draft);
        Assert.Equal(before.Jobs.Active + 2, after.Jobs.Active);
        Assert.Equal(before.Jobs.Expired + 1, after.Jobs.Expired);
        Assert.Equal(before.Jobs.Closed, after.Jobs.Closed);
        Assert.Equal(before.Applications.Total + 7, after.Applications.Total);
        Assert.Equal(7, after.Applications.ByStatus.Count);
        Assert.Equal(after.Applications.Total, after.Applications.ByStatus.Sum(b => b.Count));
        Assert.Equal(
            before.Applications.ByStatus.Single(b => b.Status == ApplicationStatus.Hired).Count + 1,
            after.Applications.ByStatus.Single(b => b.Status == ApplicationStatus.Hired).Count);
    }

    [Fact]
    public async Task Admin_TopJobs_AreLimitedToTenOrderedByViewsWhenNobodyApplied()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var jobIds = new List<int>();
        for (var i = 0; i < 11; i++)
        {
            jobIds.Add((await _api.CreateEmployerJobAsync(employer)).Id);
        }

        await _api.QueryDbAsync(async db =>
        {
            for (var i = 0; i < jobIds.Count; i++)
            {
                db.DailyViews.Add(new JobOfferDailyViews { JobOfferId = jobIds[i], Date = new DateOnly(2022, 1, 5), Views = i + 1 });
            }

            await db.SaveChangesAsync();
            return 0;
        });

        using var admin = await _api.AdminClientAsync();
        var stats = await (await admin.GetAsync("/api/admin/stats?from=2022-01-01&to=2022-01-31")).ReadJsonAsync<AdminStatsDto>();

        Assert.Equal(10, stats.TopJobs.Count);
        Assert.Equal(jobIds.AsEnumerable().Reverse().Take(10), stats.TopJobs.Select(j => j.JobId));
        Assert.DoesNotContain(jobIds[0], stats.TopJobs.Select(j => j.JobId));
        Assert.Equal(31, stats.Series.Count);
    }

    // ---------- kontrakt JSON ----------

    [Fact]
    public async Task Responses_UseCamelCaseDaysAsTextAndEnumsAsNumbers()
    {
        var s = await _api.ScenarioAsync();

        using var admin = await _api.AdminClientAsync();
        using var adminJson = JsonDocument.Parse(await (await admin.GetAsync($"/api/admin/stats?{Range}")).Content.ReadAsStringAsync());
        var root = adminJson.RootElement;
        Assert.Equal("2024-05-01", root.GetProperty("range").GetProperty("from").GetString());
        Assert.Equal(
            new[] { "date", "newApplications", "newJobs", "newUsers", "jobViews" },
            root.GetProperty("series")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            new[] { "jobId", "title", "company", "applications", "views" },
            root.GetProperty("topJobs")[0].EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            new[] { "submitted", "inReview", "interview", "offered", "hired" },
            root.GetProperty("funnel").EnumerateObject().Select(p => p.Name));
        Assert.Equal(
            new[] { "candidates", "employers", "admins" },
            root.GetProperty("totals").GetProperty("users").EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Number, root.GetProperty("totals").GetProperty("applications").GetProperty("byStatus")[0].GetProperty("status").ValueKind);

        using var client = _api.CreateClient(s.Employer);
        using var employerJson = JsonDocument.Parse(await (await client.GetAsync($"/api/employer/stats?{Range}")).Content.ReadAsStringAsync());
        var job = employerJson.RootElement.GetProperty("jobs")[0];
        Assert.Equal(
            new[] { "jobId", "title", "status", "isExpired", "views", "applications", "conversionRate" },
            job.EnumerateObject().Select(p => p.Name));
        Assert.Equal(JsonValueKind.Number, job.GetProperty("status").ValueKind);
        Assert.Equal(
            new[] { "jobs", "applications", "views" },
            employerJson.RootElement.GetProperty("totals").EnumerateObject().Select(p => p.Name));
    }

    // ---------- zakres dat ----------

    [Fact]
    public async Task NoParameters_UseTheLast30DaysEndingToday()
    {
        var s = await _api.ScenarioAsync();
        var todayBefore = DateOnly.FromDateTime(DateTime.UtcNow);

        using var admin = await _api.AdminClientAsync();
        var adminStats = await (await admin.GetAsync("/api/admin/stats")).ReadJsonAsync<AdminStatsDto>();
        var employerStats = await EmployerStatsAsync(s.Employer, "");

        var todayAfter = DateOnly.FromDateTime(DateTime.UtcNow);
        foreach (var range in new[] { adminStats.Range, employerStats.Range })
        {
            Assert.Contains(range.To, new[] { todayBefore, todayAfter });
            Assert.Equal(range.To.AddDays(-29), range.From);
        }

        Assert.Equal(30, adminStats.Series.Count);
        Assert.Equal(30, employerStats.Series.Count);
    }

    [Theory]
    [InlineData("from=2024-05-10&to=2024-05-09", "Invalid date range")]
    [InlineData("from=2024-05-1&to=2024-05-09", "Invalid date range")]
    [InlineData("from=abc", "Invalid date range")]
    [InlineData("from=2023-05-10&to=2024-05-10", "Date range cannot exceed 366 days")]
    [InlineData("from=2020-01-01&to=2024-12-31", "Date range cannot exceed 366 days")]
    public async Task InvalidRange_Returns400WithMessage_ForStatsAndExports(string query, string message)
    {
        var s = await _api.ScenarioAsync();
        using var admin = await _api.AdminClientAsync();
        using var employer = _api.CreateClient(s.Employer);

        foreach (var (client, path) in new[]
                 {
                     (admin, "/api/admin/stats"), (admin, "/api/admin/stats/export"),
                     (employer, "/api/employer/stats"), (employer, "/api/employer/stats/export")
                 })
        {
            var response = await client.GetAsync($"{path}?{query}");

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(message, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Range_Of366Days_IsAccepted()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync("/api/admin/stats?from=2023-05-11&to=2024-05-10");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(366, (await response.ReadJsonAsync<AdminStatsDto>()).Series.Count);
    }

    // ---------- autoryzacja ----------

    [Theory]
    [InlineData("/api/admin/stats")]
    [InlineData("/api/admin/stats/export")]
    [InlineData("/api/employer/stats")]
    [InlineData("/api/employer/stats/export")]
    public async Task Anonymous_Gets401(string path)
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/stats")]
    [InlineData("/api/admin/stats/export")]
    [InlineData("/api/employer/stats")]
    [InlineData("/api/employer/stats/export")]
    public async Task Candidate_Gets403(string path)
    {
        using var client = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/admin/stats")]
    [InlineData("/api/admin/stats/export")]
    public async Task Employer_CannotReadAdminStats(string path)
    {
        var s = await _api.ScenarioAsync();
        using var client = _api.CreateClient(s.Employer);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/employer/stats")]
    [InlineData("/api/employer/stats/export")]
    public async Task Admin_CannotReadEmployerStats(string path)
    {
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await admin.GetAsync(path)).StatusCode);
    }

    [Theory]
    [InlineData("/api/employer/stats")]
    [InlineData("/api/employer/stats/export")]
    public async Task EmployerWithoutCompany_Gets403WithMessage(string path)
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.QueryDbAsync(async db =>
        {
            await db.Users.Where(u => u.Id == employer.Id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CompanyId, (int?)null));
            return 0;
        });
        using var client = _api.CreateClient(employer);

        var response = await client.GetAsync(path);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NoCompanyMessage, await response.ReadMessageAsync());
    }

    // ---------- eksport CSV ----------

    private static void AssertCsvDownload(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType?.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("statystyki-2024-05-01_2024-05-10.csv", response.Content.Headers.ContentDisposition?.FileNameStar);
    }

    private static async Task<(byte[] Bytes, string[] Lines)> ReadCsvAsync(HttpResponseMessage response)
    {
        var bytes = await response.Content.ReadAsByteArrayAsync();
        var text = Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return (bytes, text.TrimEnd('\r', '\n').Split("\r\n"));
    }

    [Fact]
    public async Task EmployerExport_ReturnsTheSeriesAsCsvWithBomAndSemicolons()
    {
        var s = await _api.ScenarioAsync();
        using var client = _api.CreateClient(s.Employer);

        var response = await client.GetAsync($"/api/employer/stats/export?{Range}");

        AssertCsvDownload(response);
        var (bytes, lines) = await ReadCsvAsync(response);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(11, lines.Length);
        Assert.Equal("Data;Nowe aplikacje;Wyświetlenia ofert", lines[0]);
        Assert.Equal("2024-05-01;0;0", lines[1]);
        Assert.Equal("2024-05-02;2;5", lines[2]);
        Assert.Equal("2024-05-04;2;5", lines[4]);
        Assert.Equal("2024-05-10;0;0", lines[10]);
    }

    [Fact]
    public async Task AdminExport_ReturnsTheSeriesAsCsv()
    {
        await _api.ScenarioAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"/api/admin/stats/export?{Range}");

        AssertCsvDownload(response);
        var (bytes, lines) = await ReadCsvAsync(response);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(11, lines.Length);
        Assert.Equal("Data;Nowe aplikacje;Nowe oferty;Nowi użytkownicy;Wyświetlenia ofert", lines[0]);
        Assert.Equal("2024-05-01;0;0;4;0", lines[1]);
        Assert.Equal("2024-05-02;2;2;0;5", lines[2]);
        Assert.Equal("2024-05-04;3;1;0;9", lines[4]);
    }

    [Fact]
    public async Task EmployerExport_ContainsOnlyOwnCompanyData()
    {
        var s = await _api.ScenarioAsync();
        using var client = _api.CreateClient(s.Other);

        var (_, lines) = await ReadCsvAsync(await client.GetAsync($"/api/employer/stats/export?{Range}"));

        Assert.Equal("2024-05-04;1;4", lines[4]);
        Assert.Equal("2024-05-02;0;0", lines[2]);
    }
}
