using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Widoczność ofert zależy od firmy: oferty firm niezatwierdzonych są niewidoczne publicznie
// (lista, szczegóły, aplikowanie), a lista ofert ma filtr companyId i wyszukiwanie/sortowanie po nazwie firmy.
[Collection(ApiCollection.Name)]
public class CompanyJobVisibilityTests
{
    private const string NotFoundMessage = "Job offer not found";

    private readonly ApiFixture _api;

    public CompanyJobVisibilityTests(ApiFixture api) => _api = api;

    // Aktywna oferta w firmie oczekującej (tylko admin może opublikować ofertę w firmie niezatwierdzonej)
    private async Task<(TestEmployer Employer, JobOfferDto Job)> ActiveJobInPendingCompanyAsync()
    {
        var employer = await _api.RegisterEmployerAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = employer.CompanyId);
        return (employer, job);
    }

    private async Task<JobOfferDto[]> PublicJobsAsync(string query)
    {
        using var client = _api.CreateClient();
        var response = await client.GetAsync("/api/jobs" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto[]>();
    }

    private static string Q(string key, string value) => $"?{key}={Uri.EscapeDataString(value)}&pageSize=50";

    // --- oferty firm niezatwierdzonych ---

    [Fact]
    public async Task PublicList_ExcludesOffersOfPendingCompany()
    {
        var (employer, job) = await ActiveJobInPendingCompanyAsync();

        var byCompany = await PublicJobsAsync($"?companyId={employer.CompanyId}");
        var byTitle = await PublicJobsAsync(Q("searchTerm", job.Title));

        Assert.Empty(byCompany);
        Assert.Empty(byTitle);
    }

    [Fact]
    public async Task PublicList_ExcludesOffersOfRejectedCompany()
    {
        var (employer, job) = await ActiveJobInPendingCompanyAsync();
        await _api.RejectCompanyAsync(employer.CompanyId);

        Assert.Empty(await PublicJobsAsync(Q("searchTerm", job.Title)));
    }

    [Fact]
    public async Task PublicList_TotalItemsDoesNotCountOffersOfNonApprovedCompanies()
    {
        var (employer, _) = await ActiveJobInPendingCompanyAsync();
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs?companyId={employer.CompanyId}");

        Assert.Equal(0, response.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task Get_OfferOfPendingCompany_IsNotFoundForAnonymousUserAndOtherEmployer()
    {
        var (_, job) = await ActiveJobInPendingCompanyAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();
        using var otherEmployer = _api.CreateClient(other);

        foreach (var client in new[] { anonymous, user, otherEmployer })
        {
            var response = await client.GetAsync($"/api/jobs/{job.Id}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(NotFoundMessage, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Get_OfferOfPendingCompany_IsVisibleToAdminAndOwnEmployer()
    {
        var (employer, job) = await ActiveJobInPendingCompanyAsync();
        using var admin = await _api.AdminClientAsync();
        using var member = _api.CreateClient(employer);

        foreach (var client in new[] { admin, member })
        {
            var response = await client.GetAsync($"/api/jobs/{job.Id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.ReadJsonAsync<JobOfferDto>();
            Assert.Equal(job.Id, dto.Id);
            Assert.Equal(employer.CompanyName, dto.Company);
        }
    }

    [Fact]
    public async Task Get_OfferOfPendingCompany_LooksLikeMissingOfferToAnonymous()
    {
        var (_, job) = await ActiveJobInPendingCompanyAsync();
        using var anonymous = _api.CreateClient();

        var hidden = await anonymous.GetAsync($"/api/jobs/{job.Id}");
        var missing = await anonymous.GetAsync($"/api/jobs/{int.MaxValue}");

        Assert.Equal(missing.StatusCode, hidden.StatusCode);
        Assert.Equal(await missing.ReadMessageAsync(), await hidden.ReadMessageAsync());
    }

    [Fact]
    public async Task Apply_ToOfferOfPendingCompany_ReturnsNotFoundAndCreatesNoApplication()
    {
        var (_, job) = await ActiveJobInPendingCompanyAsync();
        var candidate = await _api.RegisterUserAsync();
        using var client = _api.CreateClient(candidate);

        var response = await client.PostJsonAsync("/api/jobapplications", new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, await response.ReadMessageAsync());
        var mine = await (await client.GetAsync("/api/jobapplications/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Empty(mine);
    }

    [Fact]
    public async Task Apply_ToOfferOfRejectedCompany_ReturnsNotFound()
    {
        var (employer, job) = await ActiveJobInPendingCompanyAsync();
        await _api.RejectCompanyAsync(employer.CompanyId);
        var candidate = await _api.RegisterUserAsync();
        using var client = _api.CreateClient(candidate);

        var response = await client.PostJsonAsync("/api/jobapplications", new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- zmiana statusu firmy po opublikowaniu ofert ---

    [Fact]
    public async Task RejectingApprovedCompany_HidesItsPublishedOffers_AndApprovingBringsThemBack()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var anonymous = _api.CreateClient();
        Assert.Single(await PublicJobsAsync($"?companyId={employer.CompanyId}"));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/jobs/{job.Id}")).StatusCode);

        await _api.RejectCompanyAsync(employer.CompanyId);

        Assert.Empty(await PublicJobsAsync($"?companyId={employer.CompanyId}"));
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/jobs/{job.Id}")).StatusCode);

        await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.Single(await PublicJobsAsync($"?companyId={employer.CompanyId}"));
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task ApprovingCompany_PublishesItsOffersCreatedByAdminBeforeApproval()
    {
        var (employer, job) = await ActiveJobInPendingCompanyAsync();
        Assert.Empty(await PublicJobsAsync(Q("searchTerm", job.Title)));

        await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.Equal(job.Id, Assert.Single(await PublicJobsAsync(Q("searchTerm", job.Title))).Id);
    }

    [Fact]
    public async Task Candidate_KeepsAccessToApplicationAfterCompanyIsRejected()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync();
        var application = await _api.ApplyAsync(candidate, job.Id);

        await _api.RejectCompanyAsync(employer.CompanyId);

        using var client = _api.CreateClient(candidate);
        var mine = await (await client.GetAsync("/api/jobapplications/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(application.Id, Assert.Single(mine).Id);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync($"/api/jobapplications/{application.Id}")).StatusCode);
    }

    // --- szkice i oferty zamknięte zatwierdzonej firmy ---

    [Fact]
    public async Task Get_DraftOfApprovedCompany_IsVisibleOnlyToAdminAndOwnEmployer()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var anonymous = _api.CreateClient();
        using var admin = await _api.AdminClientAsync();
        using var member = _api.CreateClient(employer);
        using var otherEmployer = _api.CreateClient(other);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/jobs/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await otherEmployer.GetAsync($"/api/jobs/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/jobs/{draft.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await member.GetAsync($"/api/jobs/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Get_ClosedOfferOfApprovedCompany_StaysVisibleToEveryone()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var closed = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Closed);
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"/api/jobs/{closed.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JobOfferStatus.Closed, (await response.ReadJsonAsync<JobOfferDto>()).Status);
    }

    // --- dto ---

    [Fact]
    public async Task PublicList_ExposesCompanyNameAndCompanyId()
    {
        var company = await _api.CreateCompanyAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = company.Id);

        var item = Assert.Single(await PublicJobsAsync(Q("searchTerm", job.Title)));

        Assert.Equal(company.Id, item.CompanyId);
        Assert.Equal(company.Name, item.Company);
    }

    // --- filtr companyId ---

    [Fact]
    public async Task PublicList_FilterByCompanyId_ReturnsOnlyOffersOfThatCompany()
    {
        var first = await _api.CreateCompanyAsync();
        var second = await _api.CreateCompanyAsync();
        for (var i = 0; i < 3; i++) await _api.CreateJobAsync(j => j.CompanyId = first.Id);
        await _api.CreateJobAsync(j => j.CompanyId = second.Id);

        var items = await PublicJobsAsync($"?companyId={first.Id}");

        Assert.Equal(3, items.Length);
        Assert.All(items, i => Assert.Equal(first.Id, i.CompanyId));
    }

    [Fact]
    public async Task PublicList_FilterByCompanyId_SkipsDraftsClosedAndExpired()
    {
        var company = await _api.CreateCompanyAsync();
        var active = await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, customize: j => j.CompanyId = company.Id);
        await _api.CreateExpiredJobAsync(j => j.CompanyId = company.Id);

        var items = await PublicJobsAsync($"?companyId={company.Id}");

        Assert.Equal(active.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task PublicList_FilterByCompanyId_PaginatesWithinCompany()
    {
        var company = await _api.CreateCompanyAsync();
        for (var i = 0; i < 5; i++) await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs?companyId={company.Id}&pageSize=2&pageNumber=3");

        var pagination = response.ReadPagination();
        Assert.Equal(5, pagination.TotalItems);
        Assert.Equal(3, pagination.TotalPages);
        Assert.Single(await response.ReadJsonAsync<JobOfferDto[]>());
    }

    [Fact]
    public async Task PublicList_FilterByUnknownCompanyId_ReturnsEmptyList()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs?companyId={int.MaxValue}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadJsonAsync<JobOfferDto[]>());
        Assert.Equal(0, response.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task PublicList_FilterByCompanyIdCombinesWithOtherFilters()
    {
        var company = await _api.CreateCompanyAsync();
        var remote = await _api.CreateJobAsync(j => { j.CompanyId = company.Id; j.WorkType = WorkType.Remote; });
        await _api.CreateJobAsync(j => { j.CompanyId = company.Id; j.WorkType = WorkType.Onsite; });

        var items = await PublicJobsAsync($"?companyId={company.Id}&workType={(int)WorkType.Remote}");

        Assert.Equal(remote.Id, Assert.Single(items).Id);
    }

    // --- searchTerm i sortowanie po nazwie firmy ---

    [Fact]
    public async Task PublicList_SearchTerm_MatchesCompanyNameCaseInsensitively()
    {
        var token = ApiFixture.UniqueName("Szukana");
        var company = await _api.CreateCompanyAsync(token + " Sp. z o.o.");
        var job = await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobAsync();

        var items = await PublicJobsAsync(Q("searchTerm", token.ToUpperInvariant()));

        Assert.Equal(job.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task PublicList_SearchTerm_DoesNotMatchCompanyDescriptionOrLocation()
    {
        var token = ApiFixture.UniqueName("Ukryta").Replace(" ", string.Empty);
        var company = await _api.CreateCompanyAsync(customize: c =>
        {
            c.Description = token;
            c.Location = token;
        });
        await _api.CreateJobAsync(j => j.CompanyId = company.Id);

        Assert.Empty(await PublicJobsAsync(Q("searchTerm", token)));
    }

    [Fact]
    public async Task PublicList_SearchTerm_DoesNotFindOffersByNameOfNonApprovedCompany()
    {
        var (employer, _) = await ActiveJobInPendingCompanyAsync();

        Assert.Empty(await PublicJobsAsync(Q("searchTerm", employer.CompanyName)));
    }

    [Fact]
    public async Task PublicList_OrderByCompany_SortsByCompanyName()
    {
        var prefix = ApiFixture.UniqueName("Sortowanie");
        var companies = new Dictionary<string, int>();
        foreach (var suffix in new[] { "B", "C", "A" })
        {
            companies[suffix] = (await _api.CreateCompanyAsync($"{prefix} {suffix}")).Id;
        }

        foreach (var suffix in new[] { "B", "C", "A", "B" })
        {
            await _api.CreateJobAsync(j => j.CompanyId = companies[suffix]);
        }

        var asc = await PublicJobsAsync(Q("searchTerm", prefix) + "&orderBy=companyAsc");
        var desc = await PublicJobsAsync(Q("searchTerm", prefix) + "&orderBy=companyDesc");

        Assert.Equal(
            new[] { $"{prefix} A", $"{prefix} B", $"{prefix} B", $"{prefix} C" },
            asc.Select(j => j.Company));
        Assert.Equal(
            new[] { $"{prefix} C", $"{prefix} B", $"{prefix} B", $"{prefix} A" },
            desc.Select(j => j.Company));
    }

    [Fact]
    public async Task PublicList_OrderByCompany_IsStableAcrossPages()
    {
        var company = await _api.CreateCompanyAsync();
        for (var i = 0; i < 5; i++) await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        using var client = _api.CreateClient();

        var ids = new List<int>();
        for (var page = 1; page <= 3; page++)
        {
            var response = await client.GetAsync($"/api/jobs?companyId={company.Id}&orderBy=companyAsc&pageSize=2&pageNumber={page}");
            ids.AddRange((await response.ReadJsonAsync<JobOfferDto[]>()).Select(j => j.Id));
        }

        Assert.Equal(5, ids.Distinct().Count());
    }

    // --- panel admina ---

    [Fact]
    public async Task Manage_AsAdmin_FilterByCompanyId_ShowsAllStatesAndNonApprovedCompanies()
    {
        var employer = await _api.RegisterEmployerAsync();
        var active = await _api.CreateJobAsync(j => j.CompanyId = employer.CompanyId);
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = employer.CompanyId);
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, customize: j => j.CompanyId = employer.CompanyId);
        await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"/api/jobs/manage?companyId={employer.CompanyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.ReadJsonAsync<JobOfferDto[]>();
        Assert.Equal(
            new[] { active.Id, draft.Id, closed.Id }.OrderBy(i => i),
            items.Select(j => j.Id).OrderBy(i => i));
        Assert.All(items, j => Assert.Equal(employer.CompanyName, j.Company));
    }

    [Fact]
    public async Task Manage_AsAdmin_WithoutCompanyFilter_IncludesOffersOfSeveralCompanies()
    {
        var first = await _api.CreateCompanyAsync();
        var second = await _api.RegisterEmployerAsync();
        var firstJob = await _api.CreateJobAsync(j => j.CompanyId = first.Id);
        var secondJob = await _api.CreateJobAsync(j => j.CompanyId = second.CompanyId);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync("/api/jobs/manage?pageSize=50&orderBy=newest");

        var ids = (await response.ReadJsonAsync<JobOfferDto[]>()).Select(j => j.Id).ToList();
        Assert.Contains(firstJob.Id, ids);
        Assert.Contains(secondJob.Id, ids);
    }
}
