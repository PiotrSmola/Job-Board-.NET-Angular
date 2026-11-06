using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Aplikacje z perspektywy pracodawcy: lista /all (także dla jednej oferty), szczegóły, zmiana statusu, usuwanie i aplikowanie
[Collection(ApiCollection.Name)]
public class EmployerApplicationsTests
{
    private const string Url = "/api/jobapplications";
    private const string ApplicationNotFound = "Application not found";

    private readonly ApiFixture _api;

    public EmployerApplicationsTests(ApiFixture api) => _api = api;

    private sealed record Setup(TestEmployer Employer, JobOfferDto Job, TestUser Candidate, JobApplicationDto Application);

    // Pracodawca zatwierdzonej firmy z ofertą i jedną aplikacją kandydata
    private async Task<Setup> NewSetupAsync()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync("cand");
        var application = await _api.ApplyAsync(candidate, job.Id, "Moja notatka");
        return new Setup(employer, job, candidate, application);
    }

    private async Task<JobApplicationDto[]> AllAsync(HttpClient client, string query = "")
    {
        var response = await client.GetAsync($"{Url}/all{query}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobApplicationDto[]>();
    }

    // --- /all ---

    [Fact]
    public async Task All_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Url}/all")).StatusCode);
    }

    [Fact]
    public async Task All_AsRegularUser_ReturnsForbidden()
    {
        using var client = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/all")).StatusCode);
    }

    [Fact]
    public async Task All_AsEmployer_ReturnsOnlyApplicationsToOwnCompanyOffers()
    {
        var mine = await NewSetupAsync();
        var theirs = await NewSetupAsync();
        using var client = _api.CreateClient(mine.Employer);

        var items = await AllAsync(client, "?pageSize=50");

        var item = Assert.Single(items);
        Assert.Equal(mine.Application.Id, item.Id);
        Assert.DoesNotContain(items, a => a.Id == theirs.Application.Id);
    }

    [Fact]
    public async Task All_AsEmployer_ExposesCompanyDataAndCandidate()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var item = Assert.Single(await AllAsync(client));

        Assert.Equal(setup.Employer.CompanyId, item.CompanyId);
        Assert.Equal(setup.Employer.CompanyName, item.Company);
        Assert.Equal(setup.Job.Id, item.JobOfferId);
        Assert.Equal(setup.Job.Title, item.JobTitle);
        Assert.Equal(setup.Candidate.Email, item.UserEmail);
        Assert.Equal("Moja notatka", item.Note);
        Assert.Null(item.History);
    }

    [Fact]
    public async Task All_AsEmployer_PaginatesOwnApplications()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        for (var i = 0; i < 5; i++) await _api.ApplyAsync(await _api.RegisterUserAsync(), job.Id);
        var foreign = await NewSetupAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.GetAsync($"{Url}/all?pageSize=2&pageNumber=3");

        var pagination = response.ReadPagination();
        Assert.Equal(5, pagination.TotalItems);
        Assert.Equal(3, pagination.TotalPages);
        Assert.Single(await response.ReadJsonAsync<JobApplicationDto[]>());
    }

    [Fact]
    public async Task All_AsEmployer_JobOfferIdOfAnotherCompany_ReturnsEmptyList()
    {
        var mine = await NewSetupAsync();
        var theirs = await NewSetupAsync();
        using var client = _api.CreateClient(mine.Employer);

        var items = await AllAsync(client, $"?jobOfferId={theirs.Job.Id}");

        Assert.Empty(items);
    }

    [Fact]
    public async Task All_AsEmployer_SearchTermAndStatusFilter_StayWithinOwnCompany()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var first = await _api.RegisterUserAsync("alpha");
        var second = await _api.RegisterUserAsync("beta");
        var firstApplication = await _api.ApplyAsync(first, job.Id);
        await _api.ApplyAsync(second, job.Id);
        using var employerClient = _api.CreateClient(employer);
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.ChangeApplicationStatusAsync(employerClient, firstApplication.Id, ApplicationStatus.InReview)).StatusCode);
        var foreign = await NewSetupAsync();
        using var foreignClient = _api.CreateClient(foreign.Employer);
        Assert.Equal(HttpStatusCode.OK, (await ApiFixture.ChangeApplicationStatusAsync(foreignClient, foreign.Application.Id, ApplicationStatus.InReview)).StatusCode);

        var byEmail = await AllAsync(employerClient, $"?searchTerm={Uri.EscapeDataString(first.Email)}");
        var inReview = await AllAsync(employerClient, $"?status={(int)ApplicationStatus.InReview}");
        var byForeignEmail = await AllAsync(employerClient, $"?searchTerm={Uri.EscapeDataString(foreign.Candidate.Email)}");

        Assert.Equal(firstApplication.Id, Assert.Single(byEmail).Id);
        Assert.Equal(firstApplication.Id, Assert.Single(inReview).Id);
        Assert.Empty(byForeignEmail);
    }

    [Fact]
    public async Task All_AsAdmin_ReturnsApplicationsOfAllCompanies()
    {
        var first = await NewSetupAsync();
        var second = await NewSetupAsync();
        using var admin = await _api.AdminClientAsync();

        var items = await AllAsync(admin, "?pageSize=50&orderBy=newest");

        var ids = items.Select(a => a.Id).ToList();
        Assert.Contains(first.Application.Id, ids);
        Assert.Contains(second.Application.Id, ids);
    }

    [Fact]
    public async Task All_AsEmployerWithNoOffers_ReturnsEmptyList()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        await NewSetupAsync();
        using var client = _api.CreateClient(employer);

        Assert.Empty(await AllAsync(client));
    }


    [Fact]
    public async Task All_SearchTerm_MatchesCompanyName()
    {
        var setup = await NewSetupAsync();
        await NewSetupAsync();
        using var admin = await _api.AdminClientAsync();
        using var employerClient = _api.CreateClient(setup.Employer);

        var asAdmin = await AllAsync(admin, $"?searchTerm={Uri.EscapeDataString(setup.Employer.CompanyName)}");
        var asEmployer = await AllAsync(employerClient, $"?searchTerm={Uri.EscapeDataString(setup.Employer.CompanyName.ToUpperInvariant())}");

        Assert.Equal(setup.Application.Id, Assert.Single(asAdmin).Id);
        Assert.Equal(setup.Application.Id, Assert.Single(asEmployer).Id);
    }

    [Fact]
    public async Task All_EmbeddedJobOffer_CarriesCompanyNameAndId()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var item = Assert.Single(await AllAsync(client));

        Assert.Equal(setup.Employer.CompanyId, item.JobOffer!.CompanyId);
        Assert.Equal(setup.Employer.CompanyName, item.JobOffer.Company);
    }

    // --- aplikacje do jednej oferty: /all?jobOfferId= (osobny endpoint /job/{id} usunięto) ---

    [Fact]
    public async Task AllByJob_AsEmployer_ReturnsApplicationsToOwnOffer()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var items = await AllAsync(client, $"?jobOfferId={setup.Job.Id}");

        Assert.Equal(setup.Application.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task AllByJob_AsAdmin_WorksForAnyCompany()
    {
        var setup = await NewSetupAsync();
        using var admin = await _api.AdminClientAsync();

        var items = await AllAsync(admin, $"?jobOfferId={setup.Job.Id}");

        Assert.Equal(setup.Application.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task RemovedByJobEndpoint_ReturnsNotFoundForEmployerAndAdmin()
    {
        var setup = await NewSetupAsync();
        using var employer = _api.CreateClient(setup.Employer);
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await employer.GetAsync($"{Url}/job/{setup.Job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/job/{setup.Job.Id}")).StatusCode);
    }

    // --- GET /{id} ---

    [Fact]
    public async Task Get_AsEmployerOfOffersCompany_ReturnsApplicationWithHistoryAndChangedBy()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);
        await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.InReview, "Przeglądamy");

        var application = await _api.GetApplicationAsync(client, setup.Application.Id);

        Assert.Equal(setup.Employer.CompanyId, application.CompanyId);
        Assert.Equal(setup.Employer.CompanyName, application.Company);
        var history = application.History!;
        Assert.Equal(2, history.Count);
        Assert.Equal(ApplicationStatus.Submitted, history[0].Status);
        Assert.Equal(ApplicationStatus.InReview, history[1].Status);
        Assert.Equal("Przeglądamy", history[1].Comment);
        Assert.Contains(setup.Employer.Email, history[1].ChangedBy);
        Assert.Contains(setup.Candidate.Email, history[0].ChangedBy);
    }

    [Fact]
    public async Task Get_AsCandidate_HidesChangedByEvenWhenEmployerChangedStatus()
    {
        var setup = await NewSetupAsync();
        using var employerClient = _api.CreateClient(setup.Employer);
        await ApiFixture.ChangeApplicationStatusAsync(employerClient, setup.Application.Id, ApplicationStatus.Interview, "Zapraszamy");
        using var candidateClient = _api.CreateClient(setup.Candidate);

        var application = await _api.GetApplicationAsync(candidateClient, setup.Application.Id);

        Assert.All(application.History!, h => Assert.Null(h.ChangedBy));
        Assert.Equal("Zapraszamy", application.History![^1].Comment);
    }

    [Fact]
    public async Task Get_AsAdmin_ShowsChangedByOfEmployerChange()
    {
        var setup = await NewSetupAsync();
        using var employerClient = _api.CreateClient(setup.Employer);
        await ApiFixture.ChangeApplicationStatusAsync(employerClient, setup.Application.Id, ApplicationStatus.InReview);
        using var admin = await _api.AdminClientAsync();

        var application = await _api.GetApplicationAsync(admin, setup.Application.Id);

        Assert.Contains(setup.Employer.Email, application.History![^1].ChangedBy);
    }

    [Fact]
    public async Task Get_AsEmployerOfAnotherCompany_ReturnsNotFound()
    {
        var mine = await NewSetupAsync();
        var theirs = await NewSetupAsync();
        using var client = _api.CreateClient(mine.Employer);

        var response = await client.GetAsync($"{Url}/{theirs.Application.Id}");
        var missing = await client.GetAsync($"{Url}/{int.MaxValue}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApplicationNotFound, await response.ReadMessageAsync());
        Assert.Equal(missing.StatusCode, response.StatusCode);
    }

    [Fact]
    public async Task Get_AsOtherCandidate_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();
        var stranger = await _api.RegisterUserAsync();
        using var client = _api.CreateClient(stranger);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/{setup.Application.Id}")).StatusCode);
    }

    [Fact]
    public async Task Get_AsEmployer_WorksAlsoAfterCompanyIsRejected()
    {
        var setup = await NewSetupAsync();
        await _api.RejectCompanyAsync(setup.Employer.CompanyId);
        using var client = _api.CreateClient(setup.Employer);

        var response = await client.GetAsync($"{Url}/{setup.Application.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- PUT /{id}/status ---

    [Fact]
    public async Task UpdateStatus_AsEmployerOfOffersCompany_ChangesStatusAndRecordsAuthor()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var response = await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.Interview, "Rozmowa we wtorek");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var application = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(ApplicationStatus.Interview, application.Status);
        var last = application.History![^1];
        Assert.Equal(ApplicationStatus.Interview, last.Status);
        Assert.Equal("Rozmowa we wtorek", last.Comment);
        Assert.Contains(setup.Employer.Email, last.ChangedBy);
    }

    [Fact]
    public async Task UpdateStatus_AsEmployer_IsVisibleToCandidate()
    {
        var setup = await NewSetupAsync();
        using var employerClient = _api.CreateClient(setup.Employer);
        await ApiFixture.ChangeApplicationStatusAsync(employerClient, setup.Application.Id, ApplicationStatus.Offered);
        using var candidateClient = _api.CreateClient(setup.Candidate);

        var mine = await (await candidateClient.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Equal(ApplicationStatus.Offered, Assert.Single(mine).Status);
    }

    [Fact]
    public async Task UpdateStatus_AsEmployerOfAnotherCompany_ReturnsNotFoundAndKeepsStatus()
    {
        var mine = await NewSetupAsync();
        var theirs = await NewSetupAsync();
        using var client = _api.CreateClient(mine.Employer);

        var response = await ApiFixture.ChangeApplicationStatusAsync(client, theirs.Application.Id, ApplicationStatus.Rejected, "Nie");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApplicationNotFound, await response.ReadMessageAsync());
        using var admin = await _api.AdminClientAsync();
        var current = await _api.GetApplicationAsync(admin, theirs.Application.Id);
        Assert.Equal(ApplicationStatus.Submitted, current.Status);
        Assert.Single(current.History!);
    }

    [Fact]
    public async Task UpdateStatus_AsEmployer_CannotWithdrawOnBehalfOfCandidate()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var response = await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.Withdrawn);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AsEmployer_WithSameStatusAndComment_AddsNote()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var noteResponse = await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.Submitted, "Notatka");
        var emptyResponse = await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.Submitted);

        Assert.Equal(HttpStatusCode.OK, noteResponse.StatusCode);
        Assert.Equal(2, (await noteResponse.ReadJsonAsync<JobApplicationDto>()).History!.Count);
        Assert.Equal(HttpStatusCode.BadRequest, emptyResponse.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AsEmployer_WithInvalidStatusValue_ReturnsBadRequest()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var response = await client.PutRawJsonAsync($"{Url}/{setup.Application.Id}/status", """{"status":99}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AfterCompanyIsRejected_EmployerCanStillManageExistingApplications()
    {
        var setup = await NewSetupAsync();
        await _api.RejectCompanyAsync(setup.Employer.CompanyId);
        using var client = _api.CreateClient(setup.Employer);

        var response = await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.Rejected, "Zamykamy rekrutację");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_WithoutToken_ReturnsUnauthorized_AndAsCandidateForbidden()
    {
        var setup = await NewSetupAsync();
        using var anonymous = _api.CreateClient();
        using var candidate = _api.CreateClient(setup.Candidate);

        Assert.Equal(HttpStatusCode.Unauthorized,
            (await ApiFixture.ChangeApplicationStatusAsync(anonymous, setup.Application.Id, ApplicationStatus.Hired)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden,
            (await ApiFixture.ChangeApplicationStatusAsync(candidate, setup.Application.Id, ApplicationStatus.Hired)).StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AsAdmin_StillWorksForEmployersApplications()
    {
        var setup = await NewSetupAsync();

        var application = await _api.SetApplicationStatusAsync(setup.Application.Id, ApplicationStatus.Hired);

        Assert.Equal(ApplicationStatus.Hired, application.Status);
    }

    // --- DELETE ---

    [Fact]
    public async Task Delete_AsEmployer_ReturnsForbiddenEvenForOwnApplication()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var response = await client.DeleteAsync($"{Url}/{setup.Application.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(setup.Application.Id, Assert.Single(await AllAsync(client)).Id);
    }

    [Fact]
    public async Task Delete_AsAdmin_RemovesApplication()
    {
        var setup = await NewSetupAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{setup.Application.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var employer = _api.CreateClient(setup.Employer);
        Assert.Empty(await AllAsync(employer));
    }

    [Fact]
    public async Task Delete_AsCandidate_ReturnsForbidden()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
    }

    // --- aplikowanie ---

    [Fact]
    public async Task Apply_AsEmployer_ReturnsForbiddenEvenToForeignOffer()
    {
        var setup = await NewSetupAsync();
        var otherEmployer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(otherEmployer);

        var response = await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = setup.Job.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Only candidates can apply to job offers", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Apply_AsEmployer_ToOwnOffer_ReturnsForbidden()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Empty(await AllAsync(client));
    }

    [Fact]
    public async Task Apply_AsEmployerOfPendingCompany_ReturnsForbidden()
    {
        var employer = await _api.RegisterEmployerAsync();
        var job = await _api.CreateJobAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Apply_AsEmployer_ToMissingOffer_ReturnsForbiddenBeforeLookup()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = int.MaxValue });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Apply_AsAdmin_ReturnsForbidden()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal("Only candidates can apply to job offers", await response.ReadMessageAsync());
    }

    // --- pozostałe punkty końcowe kandydata ---

    [Fact]
    public async Task MyApplications_AsEmployer_ReturnsEmptyList()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var response = await client.GetAsync($"{Url}/my-applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadJsonAsync<JobApplicationDto[]>());
    }

    [Fact]
    public async Task Withdraw_AsEmployer_ReturnsNotFound()
    {
        var setup = await NewSetupAsync();
        using var client = _api.CreateClient(setup.Employer);

        var response = await client.PostAsync($"{Url}/{setup.Application.Id}/withdraw", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Candidate_WithdrawnApplication_StaysVisibleToEmployerWithWithdrawnStatus()
    {
        var setup = await NewSetupAsync();
        await _api.WithdrawAsync(setup.Candidate, setup.Application.Id);
        using var client = _api.CreateClient(setup.Employer);

        var item = Assert.Single(await AllAsync(client));

        Assert.Equal(ApplicationStatus.Withdrawn, item.Status);
        var blocked = await ApiFixture.ChangeApplicationStatusAsync(client, setup.Application.Id, ApplicationStatus.Hired);
        Assert.Equal(HttpStatusCode.BadRequest, blocked.StatusCode);
    }
}
