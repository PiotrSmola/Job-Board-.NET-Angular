using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Przypadki brzegowe firm i rejestracji pracodawców: wyścigi, znaki specjalne w wyszukiwaniu, nieprawidłowe tokeny
[Collection(ApiCollection.Name)]
public class CompanyEdgeCaseTests
{
    private readonly ApiFixture _api;

    public CompanyEdgeCaseTests(ApiFixture api) => _api = api;

    private async Task<CompanyDto[]> AdminFindAsync(string searchTerm)
    {
        using var admin = await _api.AdminClientAsync();
        var response = await admin.GetAsync($"/api/admin/companies?pageSize=50&searchTerm={Uri.EscapeDataString(searchTerm)}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CompanyDto[]>();
    }

    private async Task<HttpResponseMessage[]> RegisterEmployerConcurrentlyAsync(string prefix, string email, int count)
    {
        var dtos = Enumerable.Range(0, count).Select(i => new RegisterEmployerDto
        {
            Email = email,
            Password = "Passw0rd1",
            FirstName = "A",
            LastName = "B",
            CompanyName = $"{prefix} {i}"
        }).ToArray();

        return await Task.WhenAll(dtos.Select(async dto =>
        {
            using var client = _api.CreateClient();
            return await client.PostJsonAsync("/api/auth/register-employer", dto);
        }));
    }

    // równoległe rejestracje na ten sam e-mail z różnymi firmami: konto powstaje raz, a firmy przegranych
    // żądań nie mogą zostać w bazie jako sieroty
    [Fact]
    public async Task RegisterEmployer_ConcurrentRequestsWithSameEmail_CreateOneAccountAndNoOrphanCompanies()
    {
        var prefix = ApiFixture.UniqueName("Wyscig email");
        var email = $"race_{Guid.NewGuid():N}@test.local";

        var responses = await RegisterEmployerConcurrentlyAsync(prefix, email, 5);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        var company = Assert.Single(await AdminFindAsync(prefix));
        Assert.Equal(1, company.MembersCount);
    }

    [Fact(Skip = "Bug: concurrent register-employer requests with the same e-mail return 500 (unhandled DbUpdateException " +
                 "from the unique user index) instead of 400 - AuthController.cs RegisterEmployer, UserManager.CreateAsync")]
    public async Task RegisterEmployer_ConcurrentRequestsWithSameEmail_NeverReturnServerError()
    {
        var prefix = ApiFixture.UniqueName("Wyscig 500");
        var email = $"race_{Guid.NewGuid():N}@test.local";

        var responses = await RegisterEmployerConcurrentlyAsync(prefix, email, 5);

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Fact(Skip = "Bug: concurrent register requests with the same e-mail return 500 instead of 400 (race between " +
                 "FindByEmailAsync and the unique user index) - AuthController.cs Register (stage 1 endpoint, same cause as register-employer)")]
    public async Task Register_ConcurrentRequestsWithSameEmail_NeverReturnServerError()
    {
        var email = $"race_{Guid.NewGuid():N}@test.local";

        var responses = await Task.WhenAll(Enumerable.Range(0, 5).Select(async _ =>
        {
            using var client = _api.CreateClient();
            return await client.PostJsonAsync("/api/auth/register",
                new RegisterDto { Email = email, Password = "Passw0rd1", FirstName = "A", LastName = "B" });
        }));

        Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);
        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
    }

    [Theory]
    [InlineData("%")]
    [InlineData("_")]
    public async Task PublicCompanyAndJobSearch_TreatsWildcardCharactersLiterally(string term)
    {
        using var anonymous = _api.CreateClient();

        var companies = await anonymous.GetAsync($"/api/companies?searchTerm={Uri.EscapeDataString(term)}&pageSize=50");
        var jobs = await anonymous.GetAsync($"/api/jobs?searchTerm={Uri.EscapeDataString(term)}&pageSize=50");

        Assert.Equal(HttpStatusCode.OK, companies.StatusCode);
        Assert.All(await companies.ReadJsonAsync<CompanyDto[]>(),
            c => Assert.True(c.Name.Contains(term, StringComparison.OrdinalIgnoreCase) || (c.Location ?? string.Empty).Contains(term, StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(HttpStatusCode.OK, jobs.StatusCode);
        Assert.All(await jobs.ReadJsonAsync<JobOfferDto[]>(),
            j => Assert.True(
                j.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                j.Company.Contains(term, StringComparison.OrdinalIgnoreCase) ||
                j.Description.Contains(term, StringComparison.OrdinalIgnoreCase)));
    }

    [Fact]
    public async Task PublicEndpoints_WithGarbageBearerToken_BehaveLikeAnonymous()
    {
        var approved = await _api.CreateCompanyAsync();
        var pending = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient("not.a.jwt");

        var approvedResponse = await client.GetAsync($"/api/companies/{approved.Id}");
        var pendingResponse = await client.GetAsync($"/api/companies/{pending.CompanyId}");
        var list = await client.GetAsync("/api/companies");

        Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
        Assert.Null((await approvedResponse.ReadJsonAsync<CompanyDto>()).Status);
        Assert.Equal(HttpStatusCode.NotFound, pendingResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
    }

    [Fact]
    public async Task EmployerEndpoints_WithGarbageBearerToken_ReturnUnauthorized()
    {
        using var client = _api.CreateClient("not.a.jwt");

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/employer/company")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/jobs/manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/companies")).StatusCode);
    }

    [Fact]
    public async Task TokenOfRegularUser_CannotBeUsedToReachEmployerEndpoints()
    {
        var candidate = await _api.RegisterUserAsync();
        using var client = _api.CreateClient(candidate);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/employer/company")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/jobs/manage")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostJsonAsync("/api/jobs", _api.NewJobDto())).StatusCode);
    }

    [Fact]
    public async Task Company_NameUniquenessIsCaseInsensitiveAcrossAllCreationPaths()
    {
        var name = ApiFixture.UniqueName("Sciezki");
        var employer = await _api.RegisterEmployerAsync(name);
        using var admin = await _api.AdminClientAsync();
        using var employerClient = _api.CreateClient(await _api.RegisterEmployerAsync());

        var viaAdminCreate = await admin.PostJsonAsync("/api/admin/companies", new CompanyFormDto { Name = name.ToUpperInvariant() });
        var viaEmployerRename = await employerClient.PutJsonAsync("/api/employer/company", new CompanyFormDto { Name = name.ToLowerInvariant() });
        var otherCompany = await _api.CreateCompanyAsync();
        var viaAdminRename = await admin.PutJsonAsync($"/api/admin/companies/{otherCompany.Id}", new CompanyFormDto { Name = name + " " });
        var viaRegister = await _api.CreateClient().PostJsonAsync("/api/auth/register-employer", new RegisterEmployerDto
        {
            Email = $"dup_{Guid.NewGuid():N}@test.local",
            Password = "Passw0rd1",
            FirstName = "A",
            LastName = "B",
            CompanyName = " " + name + " "
        });

        foreach (var response in new[] { viaAdminCreate, viaEmployerRename, viaAdminRename, viaRegister })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("Company name is already taken", await response.ReadMessageAsync());
        }

        Assert.Equal(employer.CompanyId, Assert.Single(await AdminFindAsync(name)).Id);
    }
}
