using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Infrastructure;

// Własna aplikacja i baza dla testów statystyk (nie współdzieli danych z kolekcją "Api").
// Scenariusz dane statystyk zakładany jest w przeszłym zakresie dat (2024-05-01..2024-05-10), więc nic poza nim
// nie wpływa na serie dzienne, lejek, medianę i tabele; sumy "totals" porównywane są jako różnica względem stanu przed.
public sealed class StatsFixture : ApiFixture
{
    public const string From = "2024-05-01";
    public const string To = "2024-05-10";
    public const string RangeQuery = $"from={From}&to={To}";

    private readonly Lazy<Task<StatsScenario>> _scenario;

    public StatsFixture() : base("Development", JobBoardFactory.TestJwtSecret, allowSeedRefresh: false)
    {
        _scenario = new Lazy<Task<StatsScenario>>(CreateScenarioAsync);
    }

    public Task<StatsScenario> ScenarioAsync() => _scenario.Value;

    public static DateTime At(string day, string time = "00:00:00") =>
        DateTime.SpecifyKind(DateTime.Parse($"{day}T{time}", System.Globalization.CultureInfo.InvariantCulture), DateTimeKind.Utc);

    private async Task<StatsScenario> CreateScenarioAsync()
    {
        // firma testowa powstaje przy pierwszym użyciu (NewJobDto) - ma istnieć przed pomiarem "przed"
        _ = DefaultCompanyId;

        AdminStatsDto before;
        using (var admin = await AdminClientAsync())
        {
            before = await (await admin.GetAsync($"/api/admin/stats?{RangeQuery}")).ReadJsonAsync<AdminStatsDto>();
        }

        var employer = await RegisterApprovedEmployerAsync();
        var other = await RegisterApprovedEmployerAsync();
        var candidates = new List<TestUser>();
        for (var i = 0; i < 6; i++)
        {
            candidates.Add(await RegisterUserAsync("stats"));
        }

        // oferty przez API, potem daty ustawione w bazie
        var jobA = await CreateEmployerJobAsync(employer);
        var jobB = await CreateEmployerJobAsync(employer, j => j.ExpiresAt = DateTime.UtcNow.AddDays(30));
        var draft = await CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        var jobD = await CreateEmployerJobAsync(other);

        await QueryDbAsync(async db =>
        {
            await SetCreatedAtAsync(db, jobA.Id, At("2024-05-02", "08:00:00"));
            await SetCreatedAtAsync(db, jobB.Id, At("2024-04-20", "08:00:00"));
            await SetCreatedAtAsync(db, draft.Id, At("2024-05-02", "23:59:59"));
            await SetCreatedAtAsync(db, jobD.Id, At("2024-05-04", "00:00:00"));
            var expiresAt = At("2024-05-20", "00:00:00");
            await db.JobOffers.Where(j => j.Id == jobB.Id).ExecuteUpdateAsync(s => s.SetProperty(j => j.ExpiresAt, expiresAt));

            // czterech kandydatów zarejestrowało się 1 maja, dwóch 3 maja
            for (var i = 0; i < candidates.Count; i++)
            {
                var createdAt = i < 4 ? At("2024-05-01", "12:00:00") : At("2024-05-03", "12:00:00");
                var id = candidates[i].Id;
                await db.Users.Where(u => u.Id == id).ExecuteUpdateAsync(s => s.SetProperty(u => u.CreatedAt, createdAt));
            }

            // aplikacje wraz z historią (autor zmiany: rekruter albo kandydat)
            var recruiter = employer.Id;
            var otherRecruiter = other.Id;

            var app1 = Apply(candidates[0], jobA.Id, "2024-05-02", "10:00:00");
            app1.ChangeStatus(ApplicationStatus.InReview, recruiter, null, At("2024-05-02", "12:00:00"));
            app1.ChangeStatus(ApplicationStatus.Interview, recruiter, null, At("2024-05-03", "10:00:00"));

            var app2 = Apply(candidates[1], jobA.Id, "2024-05-02", "23:59:59");
            app2.ChangeStatus(ApplicationStatus.Rejected, recruiter, "no", At("2024-05-03", "03:59:59"));

            var app3 = Apply(candidates[2], jobA.Id, "2024-05-04", "09:00:00");
            app3.ChangeStatus(ApplicationStatus.Withdrawn, candidates[2].Id, null, At("2024-05-04", "10:00:00"));

            var app4 = Apply(candidates[3], jobB.Id, "2024-05-04", "08:00:00");
            app4.ChangeStatus(ApplicationStatus.Hired, recruiter, null, At("2024-05-04", "18:00:00"));

            // tuż przed i tuż po zakresie
            var outBefore = Apply(candidates[4], jobA.Id, "2024-04-30", "23:59:59");
            var outAfter = Apply(candidates[5], jobA.Id, "2024-05-11", "00:00:00");

            var app5 = Apply(candidates[0], jobD.Id, "2024-05-04", "09:00:00");
            app5.ChangeStatus(ApplicationStatus.Offered, otherRecruiter, null, At("2024-05-04", "10:00:00"));

            db.JobApplications.AddRange(app1, app2, app3, app4, outBefore, outAfter, app5);

            db.DailyViews.AddRange(
                Views(jobA.Id, "2024-05-02", 5), Views(jobA.Id, "2024-05-04", 3), Views(jobA.Id, "2024-04-30", 7),
                Views(jobB.Id, "2024-05-04", 2),
                Views(jobD.Id, "2024-05-04", 4));
            await db.SaveChangesAsync();
            return 0;
        });

        AdminStatsDto after;
        using (var admin = await AdminClientAsync())
        {
            after = await (await admin.GetAsync($"/api/admin/stats?{RangeQuery}")).ReadJsonAsync<AdminStatsDto>();
        }

        return new StatsScenario(employer, other, jobA, jobB, draft, jobD, candidates, before, after);
    }

    private static JobApplication Apply(TestUser candidate, int jobId, string day, string time) =>
        JobApplication.Submit(jobId, candidate.Id, null, At(day, time));

    private static JobOfferDailyViews Views(int jobId, string day, int views) =>
        new() { JobOfferId = jobId, Date = DateOnly.ParseExact(day, "yyyy-MM-dd"), Views = views };

    private static Task<int> SetCreatedAtAsync(ApplicationDbContext db, int jobId, DateTime createdAt) =>
        db.JobOffers.Where(j => j.Id == jobId).ExecuteUpdateAsync(s => s.SetProperty(j => j.CreatedAt, createdAt));
}

public sealed record StatsScenario(
    TestEmployer Employer,
    TestEmployer Other,
    JobOfferDto JobA,
    JobOfferDto JobB,
    JobOfferDto Draft,
    JobOfferDto JobD,
    IReadOnlyList<TestUser> Candidates,
    AdminStatsDto AdminBefore,
    AdminStatsDto AdminAfter);
