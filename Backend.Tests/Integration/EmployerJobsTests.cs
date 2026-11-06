using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Zarządzanie ofertami przez pracodawcę: /api/jobs/manage, POST, PUT, PATCH status, DELETE
[Collection(ApiCollection.Name)]
public class EmployerJobsTests
{
    private const string NotApprovedMessage = "Company must be approved before publishing offers";
    private const string NotFoundMessage = "Job offer not found";

    private readonly ApiFixture _api;

    public EmployerJobsTests(ApiFixture api) => _api = api;

    private async Task<JobOfferDto[]> ManageAsync(TestEmployer employer, string query = "")
    {
        using var client = _api.CreateClient(employer);
        var response = await client.GetAsync("/api/jobs/manage" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto[]>();
    }

    private async Task<HttpResponseMessage> PostAsync(TestEmployer employer, Action<CreateJobOfferDto>? customize = null)
    {
        using var client = _api.CreateClient(employer);
        return await client.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto(customize));
    }

    private static UpdateJobOfferDto UpdateDto(JobOfferDto job, Action<UpdateJobOfferDto>? customize = null)
    {
        var dto = new UpdateJobOfferDto
        {
            Title = job.Title + " (edited)",
            Description = job.Description,
            Location = job.Location,
            WorkType = job.WorkType,
            SalaryMin = job.SalaryMin,
            SalaryMax = job.SalaryMax
        };
        customize?.Invoke(dto);
        return dto;
    }

    private async Task<HttpResponseMessage> PutAsync(HttpClient client, JobOfferDto job, Action<UpdateJobOfferDto>? customize = null) =>
        await client.PutJsonAsync($"/api/jobs/{job.Id}", UpdateDto(job, customize));

    private static Task<HttpResponseMessage> PatchStatusAsync(HttpClient client, int jobId, JobOfferStatus status) =>
        client.PatchJsonAsync($"/api/jobs/{jobId}/status", new UpdateJobOfferStatusDto { Status = status });

    // --- /api/jobs/manage ---

    [Fact]
    public async Task Manage_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/jobs/manage")).StatusCode);
    }

    [Fact]
    public async Task Manage_AsRegularUser_ReturnsForbidden()
    {
        using var client = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/jobs/manage")).StatusCode);
    }

    [Fact]
    public async Task Manage_ReturnsOnlyOffersOfOwnCompanyInAllStates()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var active = await _api.CreateEmployerJobAsync(employer);
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        var closed = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Closed);
        var expired = await _api.CreateExpiredJobAsync(j => j.CompanyId = employer.CompanyId);
        var foreign = await _api.CreateEmployerJobAsync(other);
        await _api.CreateJobAsync();

        var items = await ManageAsync(employer, "?pageSize=50");

        Assert.Equal(
            new[] { active.Id, draft.Id, closed.Id, expired.Id }.OrderBy(i => i),
            items.Select(j => j.Id).OrderBy(i => i));
        Assert.DoesNotContain(items, j => j.Id == foreign.Id);
        Assert.All(items, j =>
        {
            Assert.Equal(employer.CompanyId, j.CompanyId);
            Assert.Equal(employer.CompanyName, j.Company);
        });
    }

    [Fact]
    public async Task Manage_ReportsPaginationOfOwnOffersOnly()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        for (var i = 0; i < 5; i++) await _api.CreateEmployerJobAsync(employer);
        await _api.CreateJobAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.GetAsync("/api/jobs/manage?pageSize=2&pageNumber=2");

        var pagination = response.ReadPagination();
        Assert.Equal(5, pagination.TotalItems);
        Assert.Equal(3, pagination.TotalPages);
        Assert.Equal(2, pagination.CurrentPage);
        Assert.Equal(2, (await response.ReadJsonAsync<JobOfferDto[]>()).Length);
    }

    [Fact]
    public async Task Manage_CompanyIdOfAnotherCompany_DoesNotWidenEmployerScope()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var own = await _api.CreateEmployerJobAsync(employer);
        await _api.CreateEmployerJobAsync(other);

        var foreign = await ManageAsync(employer, $"?companyId={other.CompanyId}");
        var ownFilter = await ManageAsync(employer, $"?companyId={employer.CompanyId}");

        Assert.Empty(foreign);
        Assert.Equal(own.Id, Assert.Single(ownFilter).Id);
    }

    [Theory]
    [InlineData("draft", JobOfferStatus.Draft)]
    [InlineData("active", JobOfferStatus.Active)]
    [InlineData("closed", JobOfferStatus.Closed)]
    public async Task Manage_StateFilter_AppliesWithinOwnCompany(string state, JobOfferStatus expected)
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Active);
        await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Closed);
        await _api.CreateEmployerJobAsync(other, j => j.Status = expected);

        var items = await ManageAsync(employer, $"?state={state}");

        Assert.Equal(expected, Assert.Single(items).Status);
    }

    [Fact]
    public async Task Manage_StateExpired_ReturnsOnlyOwnExpiredOffers()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var expired = await _api.CreateExpiredJobAsync(j => j.CompanyId = employer.CompanyId);
        await _api.CreateExpiredJobAsync(j => j.CompanyId = other.CompanyId);
        await _api.CreateEmployerJobAsync(employer);

        var items = await ManageAsync(employer, "?state=expired");

        Assert.Equal(expired.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task Manage_SearchTerm_DoesNotLeakOffersOfOtherCompanies()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var token = ApiFixture.UniqueName("Wspolny").Replace(" ", string.Empty);
        var own = await _api.CreateEmployerJobAsync(employer, j => j.Title = "Own " + token);
        await _api.CreateEmployerJobAsync(other, j => j.Title = "Foreign " + token);

        var items = await ManageAsync(employer, $"?searchTerm={token}");

        Assert.Equal(own.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task Manage_EmployerOfPendingCompany_SeesOwnDrafts()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);

        var items = await ManageAsync(employer);

        Assert.Equal(draft.Id, Assert.Single(items).Id);
    }

    // --- POST ---

    [Fact]
    public async Task Create_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto())).StatusCode);
    }

    [Fact]
    public async Task Create_AsApprovedEmployer_ReturnsCreatedOfferInOwnCompany()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();

        var response = await PostAsync(employer, j => j.Title = "Employer's offer");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal("Employer's offer", job.Title);
        Assert.Equal(employer.CompanyId, job.CompanyId);
        Assert.Equal(employer.CompanyName, job.Company);
        Assert.Equal(JobOfferStatus.Active, job.Status);
        Assert.EndsWith($"/api/Jobs/{job.Id}", response.Headers.Location?.ToString());
    }

    [Fact]
    public async Task Create_AsApprovedEmployer_OfferIsPubliclyVisible()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"/api/jobs?companyId={employer.CompanyId}");

        Assert.Equal(job.Id, Assert.Single(await response.ReadJsonAsync<JobOfferDto[]>()).Id);
    }

    [Theory]
    [InlineData(int.MaxValue)]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task Create_AsEmployer_IgnoresCompanyIdFromRequest(int bogusCompanyId)
    {
        var employer = await _api.RegisterApprovedEmployerAsync();

        var response = await PostAsync(employer, j => j.CompanyId = bogusCompanyId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(employer.CompanyId, (await response.ReadJsonAsync<JobOfferDto>()).CompanyId);
    }

    [Fact]
    public async Task Create_AsEmployer_WithCompanyIdOfAnotherCompany_StillCreatesOfferInOwnCompany()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();

        var response = await PostAsync(employer, j => j.CompanyId = other.CompanyId);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(employer.CompanyId, job.CompanyId);
        Assert.Empty(await ManageAsync(other));
        Assert.Equal(job.Id, Assert.Single(await ManageAsync(employer)).Id);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(JobOfferStatus.Active)]
    public async Task Create_ActiveOfferInPendingCompany_ReturnsForbiddenWithMessageAndCreatesNothing(JobOfferStatus? status)
    {
        var employer = await _api.RegisterEmployerAsync();

        var response = await PostAsync(employer, j =>
        {
            if (status.HasValue) j.Status = status.Value;
        });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotApprovedMessage, await response.ReadMessageAsync());
        Assert.Empty(await ManageAsync(employer));
    }

    [Fact]
    public async Task Create_ActiveOfferInRejectedCompany_ReturnsForbidden()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId);

        var response = await PostAsync(employer);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotApprovedMessage, await response.ReadMessageAsync());
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Closed)]
    public async Task Create_DraftOrClosedOfferInPendingCompany_Succeeds(JobOfferStatus status)
    {
        var employer = await _api.RegisterEmployerAsync();

        var response = await PostAsync(employer, j => j.Status = status);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(status, (await response.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task Create_DraftInPendingCompany_IsNotPubliclyVisible()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/jobs/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Create_AfterApproval_AllowsPublishing()
    {
        var employer = await _api.RegisterEmployerAsync();
        Assert.Equal(HttpStatusCode.Forbidden, (await PostAsync(employer)).StatusCode);

        await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.Equal(HttpStatusCode.Created, (await PostAsync(employer)).StatusCode);
    }

    [Fact]
    public async Task Create_AsEmployer_WithInvalidData_ReturnsValidationError()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();

        var response = await PostAsync(employer, j =>
        {
            j.Title = string.Empty;
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await response.ReadValidationFieldsAsync();
        Assert.Contains("Title", fields);
    }

    [Fact]
    public async Task Create_AsEmployer_WithSalaryMinAboveMax_ReturnsValidationError()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();

        var response = await PostAsync(employer, j =>
        {
            j.SalaryMin = 10;
            j.SalaryMax = 5;
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("SalaryMin", await response.ReadValidationFieldsAsync());
        Assert.Empty(await ManageAsync(employer));
    }

    [Fact]
    public async Task Create_AsAdmin_WithoutCompanyId_ReturnsValidationError()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync("/api/jobs", _api.NewEmployerJobDto());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CompanyId", await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Create_AsAdmin_WithUnknownCompanyId_ReturnsBadRequest()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync("/api/jobs", _api.NewJobDto(j => j.CompanyId = int.MaxValue));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Company not found", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Create_AsAdmin_CanPublishInPendingCompany()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync("/api/jobs", _api.NewJobDto(j => j.CompanyId = employer.CompanyId));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(employer.CompanyId, job.CompanyId);
        Assert.Equal(JobOfferStatus.Active, job.Status);
    }

    // --- PUT ---

    [Fact]
    public async Task Update_OwnOffer_ReturnsUpdatedOffer()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await PutAsync(client, job, d =>
        {
            d.Title = "Zmieniony tytuł";
            d.Location = "Gdańsk";
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal("Zmieniony tytuł", updated.Title);
        Assert.Equal("Gdańsk", updated.Location);
        Assert.Equal(employer.CompanyId, updated.CompanyId);
        Assert.Equal(JobOfferStatus.Active, updated.Status);
    }

    [Fact]
    public async Task Update_OwnOffer_IgnoresCompanyIdFromRequest()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await PutAsync(client, job, d => d.CompanyId = other.CompanyId);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(employer.CompanyId, (await response.ReadJsonAsync<JobOfferDto>()).CompanyId);
        Assert.Empty(await ManageAsync(other));
    }

    [Fact]
    public async Task Update_AnotherCompanysOffer_ReturnsNotFoundAndLeavesItUnchanged()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var foreign = await _api.CreateEmployerJobAsync(other);
        using var client = _api.CreateClient(employer);

        var response = await PutAsync(client, foreign, d => d.Title = "Przejęta");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, await response.ReadMessageAsync());
        Assert.Equal(foreign.Title, Assert.Single(await ManageAsync(other)).Title);
    }

    [Fact]
    public async Task Update_AnotherCompanysOffer_LooksLikeMissingOffer()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var foreign = await _api.CreateEmployerJobAsync(other);
        using var client = _api.CreateClient(employer);

        var response = await PutAsync(client, foreign);
        var missing = await client.PutJsonAsync($"/api/jobs/{int.MaxValue}", UpdateDto(foreign));

        Assert.Equal(missing.StatusCode, response.StatusCode);
        Assert.Equal(await missing.ReadMessageAsync(), await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Update_AsEmployer_WithInvalidData_ReturnsValidationError()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await PutAsync(client, job, d => d.Title = new string('t', 201));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Title", await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Update_PublishingDraftInPendingCompany_ReturnsForbidden()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var client = _api.CreateClient(employer);

        var response = await PutAsync(client, draft, d => d.Status = JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotApprovedMessage, await response.ReadMessageAsync());
        Assert.Equal(JobOfferStatus.Draft, Assert.Single(await ManageAsync(employer)).Status);
    }

    [Fact]
    public async Task Update_DraftInPendingCompany_WithoutPublishing_Succeeds()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var client = _api.CreateClient(employer);

        var keep = await PutAsync(client, draft, d => d.Title = "Nadal szkic");
        var explicitDraft = await PutAsync(client, draft, d => d.Status = JobOfferStatus.Draft);

        Assert.Equal(HttpStatusCode.OK, keep.StatusCode);
        Assert.Equal(JobOfferStatus.Draft, (await keep.ReadJsonAsync<JobOfferDto>()).Status);
        Assert.Equal(HttpStatusCode.OK, explicitDraft.StatusCode);
    }

    [Fact]
    public async Task Update_ActiveOfferAfterCompanyIsRejected_ReturnsForbiddenUnlessClosedOrDrafted()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        await _api.RejectCompanyAsync(employer.CompanyId);
        using var client = _api.CreateClient(employer);

        var stillActive = await PutAsync(client, job);
        var closed = await PutAsync(client, job, d => d.Status = JobOfferStatus.Closed);

        Assert.Equal(HttpStatusCode.Forbidden, stillActive.StatusCode);
        Assert.Equal(NotApprovedMessage, await stillActive.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(JobOfferStatus.Closed, (await closed.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task Update_AsAdmin_CanMoveOfferToAnotherCompany()
    {
        var first = await _api.CreateCompanyAsync();
        var second = await _api.CreateCompanyAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = first.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await PutAsync(admin, job, d => d.CompanyId = second.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(second.Id, updated.CompanyId);
        Assert.Equal(second.Name, updated.Company);
    }

    [Fact]
    public async Task Update_AsAdmin_WithoutCompanyId_ReturnsValidationError()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await PutAsync(admin, job);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("CompanyId", await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Update_AsAdmin_WithUnknownCompany_ReturnsBadRequest()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await PutAsync(admin, job, d => d.CompanyId = int.MaxValue);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Company not found", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Update_AsRegularUser_ReturnsForbidden()
    {
        var job = await _api.CreateJobAsync();
        using var client = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await PutAsync(client, job)).StatusCode);
    }

    // --- PATCH status ---

    [Fact]
    public async Task PatchStatus_OwnOffer_ClosesAndReopensIt()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var closed = await PatchStatusAsync(client, job.Id, JobOfferStatus.Closed);
        var reopened = await PatchStatusAsync(client, job.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(JobOfferStatus.Closed, (await closed.ReadJsonAsync<JobOfferDto>()).Status);
        Assert.Equal(HttpStatusCode.OK, reopened.StatusCode);
        Assert.Equal(JobOfferStatus.Active, (await reopened.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task PatchStatus_PublishDraftInApprovedCompany_Succeeds()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var client = _api.CreateClient(employer);

        var response = await PatchStatusAsync(client, draft.Id, JobOfferStatus.Active);

        Assert.Equal(JobOfferStatus.Active, (await response.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task PatchStatus_AnotherCompanysOffer_ReturnsNotFoundAndKeepsStatus()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var foreign = await _api.CreateEmployerJobAsync(other);
        using var client = _api.CreateClient(employer);

        var response = await PatchStatusAsync(client, foreign.Id, JobOfferStatus.Closed);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, await response.ReadMessageAsync());
        Assert.Equal(JobOfferStatus.Active, Assert.Single(await ManageAsync(other)).Status);
    }

    [Fact]
    public async Task PatchStatus_PublishInPendingCompany_ReturnsForbiddenWithMessage()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var client = _api.CreateClient(employer);

        var response = await PatchStatusAsync(client, draft.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(NotApprovedMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task PatchStatus_AfterCompanyIsRejected_AllowsClosingAndDraftButNotReopening()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        await _api.RejectCompanyAsync(employer.CompanyId);
        using var client = _api.CreateClient(employer);

        var closed = await PatchStatusAsync(client, job.Id, JobOfferStatus.Closed);
        var reopened = await PatchStatusAsync(client, job.Id, JobOfferStatus.Active);
        var draft = await PatchStatusAsync(client, job.Id, JobOfferStatus.Draft);

        Assert.Equal(HttpStatusCode.OK, closed.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, reopened.StatusCode);
        Assert.Equal(HttpStatusCode.OK, draft.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_AsAdmin_CanPublishInPendingCompany()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var admin = await _api.AdminClientAsync();

        var response = await PatchStatusAsync(admin, draft.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_WithoutToken_ReturnsUnauthorized_AndAsUserForbidden()
    {
        var job = await _api.CreateJobAsync();
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await PatchStatusAsync(anonymous, job.Id, JobOfferStatus.Closed)).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PatchStatusAsync(user, job.Id, JobOfferStatus.Closed)).StatusCode);
    }

    [Fact]
    public async Task PatchStatus_OfMissingOffer_ReturnsNotFound()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        Assert.Equal(HttpStatusCode.NotFound, (await PatchStatusAsync(client, int.MaxValue, JobOfferStatus.Closed)).StatusCode);
    }

    // --- DELETE ---

    [Fact]
    public async Task Delete_OwnOffer_ReturnsNoContentAndRemovesIt()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await client.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await ManageAsync(employer));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_OwnDraftInPendingCompany_Succeeds()
    {
        var employer = await _api.RegisterEmployerAsync();
        var draft = await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);
        using var client = _api.CreateClient(employer);

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/jobs/{draft.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_AnotherCompanysOffer_ReturnsNotFoundAndKeepsOffer()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var other = await _api.RegisterApprovedEmployerAsync();
        var foreign = await _api.CreateEmployerJobAsync(other);
        using var client = _api.CreateClient(employer);

        var response = await client.DeleteAsync($"/api/jobs/{foreign.Id}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(NotFoundMessage, await response.ReadMessageAsync());
        Assert.Equal(foreign.Id, Assert.Single(await ManageAsync(other)).Id);
    }

    [Fact]
    public async Task Delete_OfferWithApplications_RemovesApplicationsToo()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var candidate = await _api.RegisterUserAsync();
        var application = await _api.ApplyAsync(candidate, job.Id);
        using var client = _api.CreateClient(employer);

        var response = await client.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        using var candidateClient = _api.CreateClient(candidate);
        Assert.Equal(HttpStatusCode.NotFound, (await candidateClient.GetAsync($"/api/jobapplications/{application.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_WithoutToken_ReturnsUnauthorized_AndAsUserForbidden()
    {
        var job = await _api.CreateJobAsync();
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await user.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);
    }
}
