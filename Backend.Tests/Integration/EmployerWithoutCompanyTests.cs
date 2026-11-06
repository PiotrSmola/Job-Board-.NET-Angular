using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Konto z rolą Employer bez przypisanej firmy (nie powinno wystąpić, ale API musi odpowiadać przewidywalnie)
[Collection(ApiCollection.Name)]
public class EmployerWithoutCompanyTests
{
    private const string NoCompanyMessage = "Employer account is not linked to a company";

    private readonly ApiFixture _api;

    public EmployerWithoutCompanyTests(ApiFixture api) => _api = api;

    private async Task<HttpClient> OrphanEmployerAsync()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.QueryDbAsync(async db =>
        {
            var user = await db.Users.SingleAsync(u => u.Id == employer.Id);
            user.CompanyId = null;
            await db.SaveChangesAsync();
            return 0;
        });
        return _api.CreateClient(employer);
    }

    [Fact]
    public async Task Manage_ReturnsForbiddenWithMessage()
    {
        using var client = await OrphanEmployerAsync();

        var response = await client.GetAsync("/api/jobs/manage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NoCompanyMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task AllApplications_ReturnsForbiddenWithMessage()
    {
        using var client = await OrphanEmployerAsync();

        var response = await client.GetAsync("/api/jobapplications/all");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NoCompanyMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task CreateJob_ReturnsForbiddenWithMessage()
    {
        using var client = await OrphanEmployerAsync();

        var response = await client.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NoCompanyMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task CompanyProfile_ReturnsNotFound()
    {
        using var client = await OrphanEmployerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/employer/company")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.PutJsonAsync("/api/employer/company", new CompanyFormDto { Name = ApiFixture.UniqueName("X") })).StatusCode);
    }

    [Fact]
    public async Task ExistingOffersAndApplications_AreNotAccessible()
    {
        var company = await _api.CreateCompanyAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        var candidate = await _api.RegisterUserAsync();
        var application = await _api.ApplyAsync(candidate, job.Id);
        using var client = await OrphanEmployerAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"/api/jobapplications/all?jobOfferId={job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/jobapplications/{application.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await ApiFixture.ChangeApplicationStatusAsync(client, application.Id, ApplicationStatus.Hired)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
    }
}
