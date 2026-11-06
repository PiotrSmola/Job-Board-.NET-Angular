using System.Net;
using Backend.Data;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

// Konta demo pracodawców z seedera: rekruter@techcorp.pl (firma zatwierdzona z ofertami)
// i rekruter@nowafirma.pl (firma oczekująca bez ofert)
[Collection(ApiCollection.Name)]
public class EmployerSeedTests
{
    private const string ApprovedEmail = DatabaseSeeder.ApprovedEmployerEmail;
    private const string PendingEmail = DatabaseSeeder.PendingEmployerEmail;
    private const string Password = DatabaseSeeder.EmployerPassword;

    private readonly ApiFixture _api;

    public EmployerSeedTests(ApiFixture api) => _api = api;

    private async Task<HttpClient> EmployerClientAsync(string email) =>
        _api.CreateClient(await _api.LoginAsync(email, Password));

    [Fact]
    public void SeedConstants_MatchDocumentedDemoAccounts()
    {
        Assert.Equal("rekruter@techcorp.pl", ApprovedEmail);
        Assert.Equal("rekruter@nowafirma.pl", PendingEmail);
        Assert.Equal("Employer123!", Password);
        Assert.Equal("TechCorp Solutions", DatabaseSeeder.ApprovedEmployerCompany);
        Assert.Equal("Nowa Firma Sp. z o.o.", DatabaseSeeder.PendingEmployerCompany);
    }

    [Theory]
    [InlineData(ApprovedEmail)]
    [InlineData(PendingEmail)]
    public async Task DemoEmployer_CanLogInWithEmployerRole(string email)
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = Password });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.Equal(email, user.Email);
        Assert.Equal(new[] { Roles.Employer }, JwtTestHelper.GetRoles(user.Token));
    }

    [Theory]
    [InlineData(ApprovedEmail)]
    [InlineData(PendingEmail)]
    public async Task DemoEmployer_WithWrongPassword_IsRejected(string email)
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = "Wrong123!" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // --- rekruter@techcorp.pl ---

    [Fact]
    public async Task ApprovedDemoEmployer_OwnsApprovedTechCorpWithOffers()
    {
        using var client = await EmployerClientAsync(ApprovedEmail);

        var response = await client.GetAsync("/api/employer/company");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal("TechCorp Solutions", company.Name);
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.Equal("Warszawa", company.Location);
        Assert.True(company.ActiveJobsCount >= 1);
    }

    [Fact]
    public async Task ApprovedDemoEmployer_ManagesOnlySeededTechCorpOffers()
    {
        using var client = await EmployerClientAsync(ApprovedEmail);

        var response = await client.GetAsync("/api/jobs/manage?pageSize=50");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var jobs = await response.ReadJsonAsync<JobOfferDto[]>();
        Assert.NotEmpty(jobs);
        Assert.All(jobs, j => Assert.Equal("TechCorp Solutions", j.Company));
        Assert.Single(jobs.Select(j => j.CompanyId).Distinct());
        Assert.Contains(jobs, j => j.Title == "Senior .NET Developer");
    }

    [Fact]
    public async Task ApprovedDemoEmployer_CanPublishAndRemoveOwnOffer()
    {
        using var client = await EmployerClientAsync(ApprovedEmail);
        var response = await client.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto());
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();

        try
        {
            Assert.Equal("TechCorp Solutions", job.Company);
            Assert.Equal(JobOfferStatus.Active, job.Status);
            using var anonymous = _api.CreateClient();
            Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
        }
        finally
        {
            Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);
        }
    }

    [Fact]
    public async Task ApprovedDemoEmployer_SeesApplicationsOnlyToTechCorpOffers()
    {
        using var client = await EmployerClientAsync(ApprovedEmail);
        var seededOffer = Assert.Single(
            await (await client.GetAsync("/api/jobs/manage?searchTerm=Senior%20.NET")).ReadJsonAsync<JobOfferDto[]>(),
            j => j.Title == "Senior .NET Developer");
        var candidate = await _api.RegisterUserAsync();
        var application = await _api.ApplyAsync(candidate, seededOffer.Id);
        var otherJob = await _api.CreateJobAsync();
        var foreign = await _api.ApplyAsync(candidate, otherJob.Id);

        var items = await (await client.GetAsync("/api/jobapplications/all?pageSize=50")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Contains(items, a => a.Id == application.Id);
        Assert.DoesNotContain(items, a => a.Id == foreign.Id);
        Assert.All(items, a => Assert.Equal("TechCorp Solutions", a.Company));
    }

    [Fact]
    public async Task TechCorp_IsListedPublicly()
    {
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync("/api/companies?searchTerm=techcorp");

        var company = Assert.Single(await response.ReadJsonAsync<CompanyDto[]>());
        Assert.Equal("TechCorp Solutions", company.Name);
        Assert.True(company.ActiveJobsCount >= 1);
    }

    // --- rekruter@nowafirma.pl ---

    [Fact]
    public async Task PendingDemoEmployer_OwnsPendingCompanyWithoutOffers()
    {
        using var client = await EmployerClientAsync(PendingEmail);

        var company = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        var jobs = await client.GetAsync("/api/jobs/manage");

        Assert.Equal("Nowa Firma Sp. z o.o.", company.Name);
        Assert.Equal(CompanyStatus.Pending, company.Status);
        Assert.Null(company.RejectionReason);
        Assert.Null(company.ReviewedAt);
        Assert.Equal(0, company.ActiveJobsCount);
        Assert.Equal(HttpStatusCode.OK, jobs.StatusCode);
        Assert.Empty(await jobs.ReadJsonAsync<JobOfferDto[]>());
    }

    [Fact]
    public async Task PendingDemoEmployer_CannotPublishButCanSaveDrafts()
    {
        using var client = await EmployerClientAsync(PendingEmail);

        var publish = await client.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto());
        var draft = await client.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto(j => j.Status = JobOfferStatus.Draft));

        Assert.Equal(HttpStatusCode.Forbidden, publish.StatusCode);
        Assert.Equal("Company must be approved before publishing offers", await publish.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.Created, draft.StatusCode);
        var job = await draft.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task PendingDemoCompany_IsHiddenPublicly_AndVisibleToAdmin()
    {
        using var client = await EmployerClientAsync(PendingEmail);
        var own = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        using var anonymous = _api.CreateClient();
        using var admin = await _api.AdminClientAsync();

        var publicDetail = await anonymous.GetAsync($"/api/companies/{own.Id}");
        var publicList = await anonymous.GetAsync("/api/companies?searchTerm=Nowa%20Firma");
        var adminList = await admin.GetAsync("/api/admin/companies?status=0&searchTerm=Nowa%20Firma");

        Assert.Equal(HttpStatusCode.NotFound, publicDetail.StatusCode);
        Assert.Empty(await publicList.ReadJsonAsync<CompanyDto[]>());
        var adminCompany = Assert.Single(await adminList.ReadJsonAsync<CompanyDto[]>());
        Assert.Equal(own.Id, adminCompany.Id);
        Assert.Equal(1, adminCompany.MembersCount);
    }

    // --- spójność seeda ---

    [Fact]
    public async Task Seed_CreatesExactlyOneEmployerPerDemoCompany()
    {
        using var admin = await _api.AdminClientAsync();

        var techCorp = await (await admin.GetAsync("/api/admin/companies?searchTerm=TechCorp%20Solutions")).ReadJsonAsync<CompanyDto[]>();
        var nowa = await (await admin.GetAsync("/api/admin/companies?searchTerm=Nowa%20Firma%20Sp")).ReadJsonAsync<CompanyDto[]>();

        Assert.Equal(1, Assert.Single(techCorp).MembersCount);
        Assert.Equal(1, Assert.Single(nowa).MembersCount);
    }

    [Fact]
    public async Task Seed_EverySeededOfferBelongsToAnApprovedCompany()
    {
        using var admin = await _api.AdminClientAsync();
        using var anonymous = _api.CreateClient();

        // najstarsze oferty to dane z seeda (oferty z testów są tworzone później)
        var jobs = await (await admin.GetAsync("/api/jobs/manage?pageSize=20&orderBy=oldest")).ReadJsonAsync<JobOfferDto[]>();

        Assert.Equal(20, jobs.Length);
        foreach (var job in jobs)
        {
            var response = await anonymous.GetAsync($"/api/companies/{job.CompanyId}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(job.Company, (await response.ReadJsonAsync<CompanyDto>()).Name);
        }
    }
}

// Reset danych (refresh-seed) odtwarza firmy i konta demo pracodawców; działa na osobnej bazie
public class EmployerSeedRefreshTests : IClassFixture<ProductionRefreshEnabledFixture>
{
    private readonly ProductionRefreshEnabledFixture _api;

    public EmployerSeedRefreshTests(ProductionRefreshEnabledFixture api) => _api = api;

    private async Task RefreshAsync()
    {
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.OK, (await admin.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
    }

    [Fact]
    public async Task RefreshSeed_RecreatesDemoEmployersAndTheirCompanies()
    {
        // dane użytkownika, które reset ma usunąć: pracodawca z firmą, firma dodana przez admina
        var employer = await _api.RegisterApprovedEmployerAsync();
        var adminCompany = await _api.CreateCompanyAsync();
        await _api.CreateEmployerJobAsync(employer);

        await RefreshAsync();

        using var anonymous = _api.CreateClient();
        var gone = await anonymous.PostJsonAsync("/api/auth/login", new LoginDto { Email = employer.Email, Password = employer.User.Password });
        Assert.Equal(HttpStatusCode.Unauthorized, gone.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/companies/{employer.CompanyId}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/companies/{adminCompany.Id}")).StatusCode);

        using var techCorp = _api.CreateClient(await _api.LoginAsync(DatabaseSeeder.ApprovedEmployerEmail, DatabaseSeeder.EmployerPassword));
        var techCorpCompany = await (await techCorp.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal("TechCorp Solutions", techCorpCompany.Name);
        Assert.Equal(CompanyStatus.Approved, techCorpCompany.Status);
        Assert.True(techCorpCompany.ActiveJobsCount >= 1);

        using var nowa = _api.CreateClient(await _api.LoginAsync(DatabaseSeeder.PendingEmployerEmail, DatabaseSeeder.EmployerPassword));
        var nowaCompany = await (await nowa.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal("Nowa Firma Sp. z o.o.", nowaCompany.Name);
        Assert.Equal(CompanyStatus.Pending, nowaCompany.Status);
        Assert.Empty(await (await nowa.GetAsync("/api/jobs/manage")).ReadJsonAsync<JobOfferDto[]>());
    }

    [Fact]
    public async Task RefreshSeed_CalledTwice_KeepsOneCompanyAndOneEmployerPerDemoAccount()
    {
        await RefreshAsync();
        await RefreshAsync();

        using var admin = await _api.AdminClientAsync();
        var companies = await (await admin.GetAsync("/api/admin/companies?pageSize=50&searchTerm=a")).ReadJsonAsync<CompanyDto[]>();
        var techCorp = Assert.Single(companies, c => c.Name == "TechCorp Solutions");
        var nowa = Assert.Single(companies, c => c.Name == "Nowa Firma Sp. z o.o.");
        Assert.Equal(1, techCorp.MembersCount);
        Assert.Equal(1, nowa.MembersCount);
    }

    [Fact]
    public async Task RefreshSeed_PublicCompanyListHasNoPendingDemoCompany_AndAdminListHasExactlyOneMore()
    {
        await RefreshAsync();
        using var anonymous = _api.CreateClient();
        using var admin = await _api.AdminClientAsync();

        var publicTotal = (await anonymous.GetAsync("/api/companies")).ReadPagination().TotalItems;
        var adminTotal = (await admin.GetAsync("/api/admin/companies")).ReadPagination().TotalItems;

        Assert.True(publicTotal >= 20);
        Assert.Equal(publicTotal + 1, adminTotal);
    }

    // Konta demo bez firmy (np. po cofnięciu i ponownym zastosowaniu migracji AddCompanies) są przy starcie dopinane do firm;
    // brakująca firma demo jest tworzona od nowa
    [Fact]
    public async Task Seed_AttachesExistingDemoEmployersWithoutCompany()
    {
        await RefreshAsync();
        var demoEmails = new[] { DatabaseSeeder.ApprovedEmployerEmail, DatabaseSeeder.PendingEmployerEmail };
        var oldPendingName = ApiFixture.UniqueName("Dawna firma demo");
        await _api.QueryDbAsync(async db =>
        {
            await db.Users.Where(u => demoEmails.Contains(u.Email!)).ExecuteUpdateAsync(u => u.SetProperty(x => x.CompanyId, (int?)null));
            // firma oczekująca "znika" (zmiana nazwy) - seeder musi utworzyć ją ponownie
            return await db.Companies.Where(c => c.Name == DatabaseSeeder.PendingEmployerCompany)
                .ExecuteUpdateAsync(c => c.SetProperty(x => x.Name, oldPendingName));
        });
        var userIds = await _api.QueryDbAsync(db => db.Users.Where(u => demoEmails.Contains(u.Email!))
            .OrderBy(u => u.Email).Select(u => u.Id).ToListAsync());

        using (var scope = _api.Factory.Services.CreateScope())
        {
            await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
        }

        using var techCorp = _api.CreateClient(await _api.LoginAsync(DatabaseSeeder.ApprovedEmployerEmail, DatabaseSeeder.EmployerPassword));
        var techCorpCompany = await (await techCorp.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(DatabaseSeeder.ApprovedEmployerCompany, techCorpCompany.Name);
        Assert.Equal(CompanyStatus.Approved, techCorpCompany.Status);
        Assert.True(techCorpCompany.ActiveJobsCount >= 1);

        using var nowa = _api.CreateClient(await _api.LoginAsync(DatabaseSeeder.PendingEmployerEmail, DatabaseSeeder.EmployerPassword));
        var nowaCompany = await (await nowa.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(DatabaseSeeder.PendingEmployerCompany, nowaCompany.Name);
        Assert.Equal(CompanyStatus.Pending, nowaCompany.Status);

        // te same konta (bez duplikatów), dopięte do firm
        var after = await _api.QueryDbAsync(db => db.Users.Where(u => demoEmails.Contains(u.Email!))
            .OrderBy(u => u.Email).Select(u => new { u.Id, u.CompanyId }).ToListAsync());
        Assert.Equal(userIds, after.Select(u => u.Id));
        Assert.All(after, u => Assert.NotNull(u.CompanyId));
    }

    [Fact]
    public async Task Seed_DoesNotReassignDemoEmployerAlreadyLinkedToACompany()
    {
        await RefreshAsync();
        var other = await _api.CreateCompanyAsync();
        await _api.QueryDbAsync(db => db.Users.Where(u => u.Email == DatabaseSeeder.ApprovedEmployerEmail)
            .ExecuteUpdateAsync(u => u.SetProperty(x => x.CompanyId, other.Id)));

        using (var scope = _api.Factory.Services.CreateScope())
        {
            await DatabaseSeeder.SeedAsync(scope.ServiceProvider);
        }

        var companyId = await _api.QueryDbAsync(db => db.Users.Where(u => u.Email == DatabaseSeeder.ApprovedEmployerEmail)
            .Select(u => u.CompanyId).SingleAsync());
        Assert.Equal(other.Id, companyId);
    }
}
