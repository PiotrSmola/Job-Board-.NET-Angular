using System.Net;
using System.Text.Json;
using Backend.Data;
using Backend.DTOs;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Środowisko Production z wyłączonym resetem danych (Admin:AllowSeedRefresh=false), bez kont demo (Seed:DemoAccounts nieustawione)
public class ProductionRefreshDisabledFixture : ApiFixture
{
    public ProductionRefreshDisabledFixture() : base("Production", JobBoardFactory.TestJwtSecret, allowSeedRefresh: false)
    {
    }
}

// Środowisko Production z jawnie włączonym resetem danych i kontami demo - jak lokalny stack docker-compose
// (Admin:AllowSeedRefresh=true, Seed:DemoAccounts=true)
public class ProductionRefreshEnabledFixture : ApiFixture
{
    public ProductionRefreshEnabledFixture()
        : base("Production", JobBoardFactory.TestJwtSecret, allowSeedRefresh: true, cacheTokens: false, demoAccounts: true)
    {
    }
}

// Środowisko Production z włączonym resetem danych, ale z jawnie wyłączonymi kontami demo (Seed:DemoAccounts=false)
public class ProductionWithoutDemoAccountsFixture : ApiFixture
{
    public ProductionWithoutDemoAccountsFixture()
        : base("Production", JobBoardFactory.TestJwtSecret, allowSeedRefresh: true, cacheTokens: false, demoAccounts: false)
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
        // poza Development bez Seed:DemoAccounts nie ma kont demo kandydatów - rejestrujemy własnego użytkownika
        using var client = _api.CreateClient(await _api.RegisterUserAsync());

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

    [Theory]
    [InlineData(TestUsers.UserEmail, TestUsers.UserPassword)]
    [InlineData(DatabaseSeeder.ApprovedEmployerEmail, DatabaseSeeder.EmployerPassword)]
    [InlineData(DatabaseSeeder.PendingEmployerEmail, DatabaseSeeder.EmployerPassword)]
    public async Task Application_InProductionWithoutDemoFlag_DoesNotCreateDemoAccounts(string email, string password)
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Application_InProductionWithoutDemoFlag_HasNoDemoPendingCompany()
    {
        var pendingCompanies = await _api.QueryDbAsync(db =>
            db.Companies.CountAsync(c => c.Name == DatabaseSeeder.PendingEmployerCompany));

        Assert.Equal(0, pendingCompanies);
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
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(DatabaseSeeder.ApprovedEmployerEmail, DatabaseSeeder.EmployerPassword)));

        // oferty to dokładnie dane demo, bez naszej oferty, bez aplikacji
        var jobs = await anonymous.GetAsync("/api/jobs?pageSize=50");
        Assert.Equal(20, jobs.ReadPagination().TotalItems);
        Assert.DoesNotContain(await jobs.ReadJsonAsync<JobOfferDto[]>(), j => j.Title == "Offer to be wiped");
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/jobs/{job.Id}")).StatusCode);

        var refreshedAdmin = await _api.AdminClientAsync();
        var all = await refreshedAdmin.GetAsync("/api/jobapplications/all");
        Assert.Equal(0, all.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task Apply_WithValidTokenOfUserDeletedByRefresh_ReturnsUnauthorized()
    {
        var user = await _api.RegisterUserAsync("stale");
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
        using var anonymous = _api.CreateClient();
        var jobs = await (await anonymous.GetAsync("/api/jobs")).ReadJsonAsync<JobOfferDto[]>();
        using var staleClient = _api.CreateClient(user);

        var response = await staleClient.PostJsonAsync("/api/jobapplications",
            new CreateJobApplicationDto { JobOfferId = jobs[0].Id });

        // token jest jeszcze ważny, ale użytkownik już nie istnieje - 401 zamiast 500 (naruszenie klucza obcego)
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await staleClient.GetAsync("/api/jobapplications/my-applications")).StatusCode);
    }

    [Fact]
    public async Task AdminToken_IssuedBeforeRefresh_IsRejectedAfterRefresh()
    {
        // reset odtwarza konto admina od nowa (nowe id i security stamp) - stary token traci ważność
        var oldAdminToken = await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword);
        using var oldAdmin = _api.CreateClient(oldAdminToken);
        Assert.Equal(HttpStatusCode.OK, (await oldAdmin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        Assert.Equal(HttpStatusCode.Unauthorized, (await oldAdmin.GetAsync("/api/admin/settings")).StatusCode);
        using var newAdmin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await newAdmin.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task RefreshSeed_CalledTwice_ProducesTheSameSeedState()
    {
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        // po resecie konto admina jest nowe - potrzebny świeży token
        using var adminAfterRefresh = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await adminAfterRefresh.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        using var anonymous = _api.CreateClient();
        Assert.Equal(20, (await anonymous.GetAsync("/api/jobs")).ReadPagination().TotalItems);
    }
}

// Seed:DemoAccounts=false: ani start, ani reset danych nie tworzą kont demo (admin zawsze jest tworzony)
public class DemoAccountsDisabledTests : IClassFixture<ProductionWithoutDemoAccountsFixture>
{
    private readonly ProductionWithoutDemoAccountsFixture _api;

    public DemoAccountsDisabledTests(ProductionWithoutDemoAccountsFixture api) => _api = api;

    private async Task AssertNoDemoAccountsAsync()
    {
        using var client = _api.CreateClient();
        foreach (var (email, password) in new[]
                 {
                     (TestUsers.UserEmail, TestUsers.UserPassword),
                     (DatabaseSeeder.ApprovedEmployerEmail, DatabaseSeeder.EmployerPassword),
                     (DatabaseSeeder.PendingEmployerEmail, DatabaseSeeder.EmployerPassword)
                 })
        {
            var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        Assert.Equal(0, await _api.QueryDbAsync(db => db.Users.CountAsync(u => u.CompanyId != null)));
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword)));
    }

    [Fact]
    public async Task StartupAndRefresh_DoNotCreateDemoAccounts()
    {
        await AssertNoDemoAccountsAsync();

        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);

        await AssertNoDemoAccountsAsync();
        using var anonymous = _api.CreateClient();
        Assert.Equal(20, (await anonymous.GetAsync("/api/jobs")).ReadPagination().TotalItems);
    }
}
