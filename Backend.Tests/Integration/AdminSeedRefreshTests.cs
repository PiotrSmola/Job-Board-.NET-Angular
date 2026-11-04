using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Środowisko Production z wyłączonym resetem danych (Admin:AllowSeedRefresh=false)
public class ProductionRefreshDisabledFixture : ApiFixture
{
    public ProductionRefreshDisabledFixture() : base("Production", JobBoardFactory.TestJwtSecret, allowSeedRefresh: false)
    {
    }
}

// Środowisko Production z jawnie włączonym resetem danych (Admin:AllowSeedRefresh=true)
public class ProductionRefreshEnabledFixture : ApiFixture
{
    public ProductionRefreshEnabledFixture() : base("Production", JobBoardFactory.TestJwtSecret, allowSeedRefresh: true)
    {
    }
}

public class AdminSeedRefreshDisabledTests : IClassFixture<ProductionRefreshDisabledFixture>
{
    private readonly ProductionRefreshDisabledFixture _api;

    public AdminSeedRefreshDisabledTests(ProductionRefreshDisabledFixture api) => _api = api;

    [Fact]
    public async Task Settings_InProductionWithFlagOff_ReportsSeedRefreshDisabled()
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.GetAsync("/api/admin/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("allowSeedRefresh").GetBoolean());
    }

    [Fact]
    public async Task RefreshSeed_InProductionWithFlagOff_ReturnsForbiddenAndKeepsData()
    {
        var user = await _api.RegisterUserAsync("keepme");
        var job = await _api.CreateJobAsync();
        await _api.ApplyAsync(user, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsync("/api/admin/refresh-seed", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Seed refresh is disabled in this environment", body.RootElement.GetProperty("message").GetString());

        // dane nietknięte: użytkownik loguje się, oferta i aplikacja istnieją
        var token = await _api.LoginAsync(user.Email, user.Password);
        using var userClient = _api.CreateClient(token);
        Assert.Equal(HttpStatusCode.OK, (await userClient.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
        var mine = await (await userClient.GetAsync("/api/jobapplications/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Single(mine);
    }

    [Fact]
    public async Task RefreshSeed_InProductionWithFlagOff_AsRegularUser_ReturnsForbidden()
    {
        using var client = await _api.UserClientAsync();

        var response = await client.PostAsync("/api/admin/refresh-seed", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Application_InProduction_StartsWithSeedDataAndAcceptsLogin()
    {
        // kontrola dodatnia: Production z jawnym sekretem startuje i seeduje dane
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(response.ReadPagination().TotalItems >= 20);
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword)));
    }
}

// Reset danych działa na osobnej bazie - usuwa wszystkich użytkowników, oferty i aplikacje
public class AdminSeedRefreshEnabledTests : IClassFixture<ProductionRefreshEnabledFixture>
{
    private readonly ProductionRefreshEnabledFixture _api;

    public AdminSeedRefreshEnabledTests(ProductionRefreshEnabledFixture api) => _api = api;

    [Fact]
    public async Task Settings_InProductionWithFlagOn_ReportsSeedRefreshEnabled()
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.GetAsync("/api/admin/settings");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("allowSeedRefresh").GetBoolean());
    }

    [Fact]
    public async Task RefreshSeed_InProductionWithFlagOn_ReplacesAllDataWithSeed()
    {
        var user = await _api.RegisterUserAsync("towipe");
        var job = await _api.CreateJobAsync(j => j.Title = "Offer to be wiped");
        await _api.ApplyAsync(user, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsync("/api/admin/refresh-seed", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Database has been refreshed with seed data", body.RootElement.GetProperty("message").GetString());
        Assert.True(body.RootElement.TryGetProperty("timestamp", out _));

        // zarejestrowany użytkownik zniknął, konta z seeda działają
        using var anonymous = _api.CreateClient();
        var loginGone = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = user.Email, Password = user.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, loginGone.StatusCode);
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword)));
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword)));

        // oferty to dokładnie dane demo, bez naszej oferty, bez aplikacji
        var jobs = await anonymous.GetAsync("/api/jobs?pageSize=50");
        Assert.Equal(20, jobs.ReadPagination().TotalItems);
        Assert.DoesNotContain(await jobs.ReadJsonAsync<JobOfferDto[]>(), j => j.Title == "Offer to be wiped");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/jobs/{job.Id}")).StatusCode);

        var refreshedAdmin = await _api.AdminClientAsync();
        var all = await refreshedAdmin.GetAsync("/api/jobapplications/all");
        Assert.Equal(0, all.ReadPagination().TotalItems);
    }

    [Fact(Skip = "Bug: a still-valid JWT of a user that no longer exists (e.g. deleted by refresh-seed) is accepted; applying then fails with a foreign key violation (DbUpdateException -> HTTP 500) in JobApplicationsController.ApplyToJob (Backend/Controllers/JobApplicationsController.cs:93). Expected 401/4xx.")]
    public async Task Apply_WithValidTokenOfUserDeletedByRefresh_ShouldNotCauseServerError()
    {
        var user = await _api.RegisterUserAsync("stale");
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
        using var anonymous = _api.CreateClient();
        var jobs = await (await anonymous.GetAsync("/api/jobs")).ReadJsonAsync<JobOfferDto[]>();
        using var staleClient = _api.CreateClient(user);

        var response = await staleClient.PostJsonAsync("/api/jobapplications",
            new CreateJobApplicationDto { JobOfferId = jobs[0].Id });

        // token jest jeszcze ważny, ale użytkownik już nie istnieje - oczekiwane 4xx, nie 500 (naruszenie klucza obcego)
        Assert.InRange((int)response.StatusCode, 400, 499);
    }

    [Fact]
    public async Task RefreshSeed_CalledTwice_ProducesTheSameSeedState()
    {
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        using var anonymous = _api.CreateClient();
        Assert.Equal(20, (await anonymous.GetAsync("/api/jobs")).ReadPagination().TotalItems);
    }
}
