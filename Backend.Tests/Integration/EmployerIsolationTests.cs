using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Izolacja dwóch pracodawców z różnych firm: żaden nie widzi ani nie zmienia ofert i aplikacji drugiego
// w panelu zarządzania, a każda próba dostępu wygląda jak odwołanie do nieistniejącego zasobu (404).
[Collection(ApiCollection.Name)]
public class EmployerIsolationTests
{
    private readonly ApiFixture _api;

    public EmployerIsolationTests(ApiFixture api) => _api = api;

    private sealed record Tenant(TestEmployer Employer, JobOfferDto Job, JobOfferDto Draft, TestUser Candidate, JobApplicationDto Application);

    private async Task<Tenant> NewTenantAsync()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        var candidate = await _api.RegisterUserAsync();
        var application = await _api.ApplyAsync(candidate, job.Id, "Notatka kandydata");
        return new Tenant(employer, job, draft, candidate, application);
    }

    private async Task<JobOfferDto[]> ManageAsync(Tenant tenant)
    {
        using var client = _api.CreateClient(tenant.Employer);
        var response = await client.GetAsync("/api/jobs/manage?pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto[]>();
    }

    private async Task<JobApplicationDto[]> ApplicationsAsync(Tenant tenant)
    {
        using var client = _api.CreateClient(tenant.Employer);
        var response = await client.GetAsync("/api/jobapplications/all?pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto[]>();
    }

    [Fact]
    public async Task Manage_EachEmployerSeesOnlyOwnOffers()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();

        var aJobs = await ManageAsync(a);
        var bJobs = await ManageAsync(b);

        Assert.Equal(new[] { a.Job.Id, a.Draft.Id }.OrderBy(i => i), aJobs.Select(j => j.Id).OrderBy(i => i));
        Assert.Equal(new[] { b.Job.Id, b.Draft.Id }.OrderBy(i => i), bJobs.Select(j => j.Id).OrderBy(i => i));
    }

    [Fact]
    public async Task Applications_EachEmployerSeesOnlyOwnApplications()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();

        Assert.Equal(a.Application.Id, Assert.Single(await ApplicationsAsync(a)).Id);
        Assert.Equal(b.Application.Id, Assert.Single(await ApplicationsAsync(b)).Id);
    }

    [Fact]
    public async Task ForeignOffer_EveryManagementEndpoint_ReturnsNotFound()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        using var client = _api.CreateClient(a.Employer);
        var updateBody = new UpdateJobOfferDto
        {
            Title = "Przejęcie",
            Description = "x",
            Location = "x",
            WorkType = WorkType.Remote,
            CompanyId = a.Employer.CompanyId
        };

        var responses = new[]
        {
            await client.PutJsonAsync($"/api/jobs/{b.Job.Id}", updateBody),
            await client.PutJsonAsync($"/api/jobs/{b.Draft.Id}", updateBody),
            await client.PatchJsonAsync($"/api/jobs/{b.Job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed }),
            await client.DeleteAsync($"/api/jobs/{b.Job.Id}"),
            await client.DeleteAsync($"/api/jobs/{b.Draft.Id}"),
            await client.GetAsync($"/api/jobapplications/job/{b.Job.Id}")
        };

        Assert.All(responses, r => Assert.Equal(HttpStatusCode.NotFound, r.StatusCode));

        // dane drugiego pracodawcy bez zmian
        var bJobs = await ManageAsync(b);
        Assert.Equal(2, bJobs.Length);
        Assert.Contains(bJobs, j => j.Id == b.Job.Id && j.Title == b.Job.Title && j.Status == JobOfferStatus.Active);
        Assert.Contains(bJobs, j => j.Id == b.Draft.Id && j.Status == JobOfferStatus.Draft);
        Assert.DoesNotContain(await ManageAsync(a), j => j.CompanyId == b.Employer.CompanyId);
    }

    [Fact]
    public async Task ForeignApplication_EveryEndpoint_ReturnsNotFound()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        using var client = _api.CreateClient(a.Employer);

        var get = await client.GetAsync($"/api/jobapplications/{b.Application.Id}");
        var status = await ApiFixture.ChangeApplicationStatusAsync(client, b.Application.Id, ApplicationStatus.Rejected, "Nie");

        Assert.Equal(HttpStatusCode.NotFound, get.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, status.StatusCode);
        var untouched = Assert.Single(await ApplicationsAsync(b));
        Assert.Equal(ApplicationStatus.Submitted, untouched.Status);
    }

    [Fact]
    public async Task ForeignApplication_DeleteIsForbiddenForEveryEmployer()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        using var client = _api.CreateClient(a.Employer);

        var response = await client.DeleteAsync($"/api/jobapplications/{b.Application.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(await ApplicationsAsync(b));
    }

    [Fact]
    public async Task CompanyProfile_EachEmployerEditsOnlyOwnCompany()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        using var client = _api.CreateClient(a.Employer);

        var response = await client.PutJsonAsync("/api/employer/company", new CompanyFormDto
        {
            Name = a.Employer.CompanyName,
            Location = "Tylko A"
        });

        Assert.Equal(a.Employer.CompanyId, (await response.ReadJsonAsync<CompanyDto>()).Id);
        using var bClient = _api.CreateClient(b.Employer);
        var bCompany = await (await bClient.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(b.Employer.CompanyId, bCompany.Id);
        Assert.Null(bCompany.Location);
    }

    [Fact]
    public async Task ForeignPendingCompany_IsInvisibleToEmployerOfAnotherCompany()
    {
        var a = await NewTenantAsync();
        var pending = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(a.Employer);

        var response = await client.GetAsync($"/api/companies/{pending.CompanyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task EmployerCannotCreateOfferInForeignCompany_EvenWithItsCompanyId()
    {
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        using var client = _api.CreateClient(a.Employer);

        var response = await client.PostJsonAsync("/api/jobs", _api.NewJobDto(j => j.CompanyId = b.Employer.CompanyId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var created = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(a.Employer.CompanyId, created.CompanyId);
        Assert.Equal(2, (await ManageAsync(b)).Length);
    }

    [Fact]
    public async Task EmployerScope_ComesFromDatabaseNotFromToken()
    {
        // zakres pracodawcy wynika z firmy zapisanej w bazie: po zmianie przypisania konta ten sam token
        // wskazuje już nową firmę (token nie zawiera identyfikatora firmy)
        var a = await NewTenantAsync();
        var b = await NewTenantAsync();
        await _api.QueryDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == a.Employer.Id);
            user.CompanyId = b.Employer.CompanyId;
            await db.SaveChangesAsync();
            return 0;
        });

        var jobs = await ManageAsync(a);

        Assert.All(jobs, j => Assert.Equal(b.Employer.CompanyId, j.CompanyId));
        Assert.Contains(jobs, j => j.Id == b.Job.Id);
    }
}
