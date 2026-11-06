using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Proces rekrutacji: aplikowanie na oferty w różnych stanach, zmiany statusu przez admina, wycofanie,
// ponowna aplikacja (reaktywacja), historia statusów i widoczność autora zmian.
[Collection(ApiCollection.Name)]
public class ApplicationWorkflowTests
{
    private const string Url = "/api/jobapplications";
    private const string NotAcceptingMessage = "This job offer is not accepting applications";
    private const string OnlyCandidatesMessage = "Only candidates can apply to job offers";

    private readonly ApiFixture _api;

    public ApplicationWorkflowTests(ApiFixture api) => _api = api;

    private async Task<(JobOfferDto Job, TestUser User, JobApplicationDto Application)> NewApplicationAsync(string? note = null)
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("candidate");
        var application = await _api.ApplyAsync(user, job.Id, note);
        return (job, user, application);
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString();
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors").Clone();
    }

    private async Task<HttpResponseMessage> TryApplyAsync(TestUser user, int jobId, string? note = null)
    {
        using var client = _api.CreateClient(user);
        return await client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = jobId, Note = note });
    }

    private async Task<JobApplicationDto> GetAsCandidateAsync(TestUser user, int applicationId)
    {
        using var client = _api.CreateClient(user);
        return await _api.GetApplicationAsync(client, applicationId);
    }

    private async Task<JobApplicationDto> GetAsAdminAsync(int applicationId)
    {
        using var admin = await _api.AdminClientAsync();
        return await _api.GetApplicationAsync(admin, applicationId);
    }

    // --- aplikowanie na oferty, które nie przyjmują zgłoszeń ---

    // szkic nie istnieje dla kandydata - ta sama odpowiedź co dla nieistniejącej oferty (jak GET /api/jobs/{id})
    [Fact]
    public async Task Apply_ToDraftOffer_ReturnsNotFoundLikeMissingOffer()
    {
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft);
        var user = await _api.RegisterUserAsync();

        var response = await TryApplyAsync(user, draft.Id);
        var missing = await TryApplyAsync(user, int.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Job offer not found", await MessageAsync(response));
        Assert.Equal(missing.StatusCode, response.StatusCode);
        Assert.Equal(await MessageAsync(missing), await MessageAsync(response));
    }

    [Fact]
    public async Task Apply_AsAdminToDraftOffer_ReturnsForbidden()
    {
        // admin nie aplikuje na oferty (tylko kandydaci) - niezależnie od stanu oferty
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = draft.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OnlyCandidatesMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task Apply_ToClosedOffer_ReturnsBadRequestNotAccepting()
    {
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed);
        var user = await _api.RegisterUserAsync();

        var response = await TryApplyAsync(user, closed.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotAcceptingMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task Apply_ToExpiredOffer_ReturnsBadRequestNotAccepting()
    {
        var expired = await _api.CreateExpiredJobAsync();
        var user = await _api.RegisterUserAsync();

        var response = await TryApplyAsync(user, expired.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotAcceptingMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task Apply_ToNotAcceptingOffer_DoesNotCreateApplication()
    {
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed);
        var user = await _api.RegisterUserAsync();
        await TryApplyAsync(user, closed.Id);

        using var client = _api.CreateClient(user);
        var mine = await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Empty(mine);
    }

    [Fact]
    public async Task Apply_ToOfferWithFutureExpiration_Succeeds()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(3));
        var user = await _api.RegisterUserAsync();

        var response = await TryApplyAsync(user, job.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Apply_ToOfferThatWasClosedAndReopened_Succeeds()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();
        await admin.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });
        var user = await _api.RegisterUserAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await TryApplyAsync(user, job.Id)).StatusCode);

        await admin.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Active });
        var response = await TryApplyAsync(user, job.Id);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task Apply_AfterOfferWasClosed_BlocksReapplyingAfterWithdrawal()
    {
        var (job, user, application) = await NewApplicationAsync();
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(user, application.Id)).StatusCode);
        using var admin = await _api.AdminClientAsync();
        await admin.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });

        var response = await TryApplyAsync(user, job.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NotAcceptingMessage, await MessageAsync(response));
        Assert.Equal(ApplicationStatus.Withdrawn, (await GetAsCandidateAsync(user, application.Id)).Status);
    }

    [Fact]
    public async Task Apply_NewApplication_StartsAsSubmittedWithSingleHistoryEntry()
    {
        var before = DateTime.UtcNow.AddSeconds(-5);

        var (_, user, application) = await NewApplicationAsync("Hello");

        Assert.Equal(ApplicationStatus.Submitted, application.Status);
        Assert.Equal(application.AppliedAt, application.StatusChangedAt);
        Assert.True(application.StatusChangedAt >= before);
        Assert.NotNull(application.History);
        var entry = Assert.Single(application.History!);
        Assert.Equal(ApplicationStatus.Submitted, entry.Status);
        Assert.Equal(application.AppliedAt, entry.ChangedAt);
        Assert.Null(entry.Comment);
        // kandydat nie widzi autora zmian
        Assert.Null(entry.ChangedBy);
        Assert.Equal(user.Id, application.UserId);
    }

    [Fact]
    public async Task Apply_AsAdminToActiveOffer_ReturnsForbiddenAndCreatesNoApplication()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(OnlyCandidatesMessage, await MessageAsync(response));
        var forJob = await (await admin.GetAsync($"{Url}/all?pageSize=50&jobOfferId={job.Id}")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Empty(forJob);
    }

    // --- PUT /api/jobapplications/{id}/status ---

    [Fact]
    public async Task UpdateStatus_WithoutToken_ReturnsUnauthorized()
    {
        var (_, _, application) = await NewApplicationAsync();
        using var client = _api.CreateClient();

        var response = await client.PutJsonAsync($"{Url}/{application.Id}/status", new UpdateApplicationStatusDto { Status = ApplicationStatus.InReview });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_AsRegularUser_ReturnsForbiddenAndKeepsStatus()
    {
        var (_, user, application) = await NewApplicationAsync();

        // zarówno właściciel aplikacji, jak i inny użytkownik
        foreach (var caller in new[] { user, await _api.RegisterUserAsync("other") })
        {
            using var client = _api.CreateClient(caller);
            var response = await client.PutJsonAsync($"{Url}/{application.Id}/status",
                new UpdateApplicationStatusDto { Status = ApplicationStatus.Hired });

            Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        }

        var current = await GetAsCandidateAsync(user, application.Id);
        Assert.Equal(ApplicationStatus.Submitted, current.Status);
        Assert.Single(current.History!);
    }

    [Fact]
    public async Task UpdateStatus_NonexistentApplication_ReturnsNotFound()
    {
        var response = await _api.ChangeApplicationStatusAsync(int.MaxValue, ApplicationStatus.InReview);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Application not found", await MessageAsync(response));
    }

    [Fact]
    public async Task UpdateStatus_ReturnsApplicationWithChronologicalHistoryAndAuthors()
    {
        var (_, user, application) = await NewApplicationAsync("Note from candidate");

        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "Looks promising");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(application.Id, updated.Id);
        Assert.Equal(ApplicationStatus.InReview, updated.Status);
        Assert.True(updated.StatusChangedAt > application.StatusChangedAt);
        Assert.Equal(application.AppliedAt, updated.AppliedAt);
        Assert.Equal("Note from candidate", updated.Note);
        Assert.Equal(user.Email, updated.UserEmail);
        Assert.Equal(application.JobTitle, updated.JobTitle);
        Assert.Equal(new[] { ApplicationStatus.Submitted, ApplicationStatus.InReview }, updated.History!.Select(h => h.Status));
        Assert.Null(updated.History![0].Comment);
        Assert.Equal("Looks promising", updated.History![1].Comment);
        Assert.Equal(updated.StatusChangedAt, updated.History![1].ChangedAt);
        // odpowiedź dla admina zawiera autorów zmian: kandydata i rekrutera
        Assert.Contains(user.Email, updated.History![0].ChangedBy);
        Assert.Contains(TestUsers.AdminEmail, updated.History![1].ChangedBy);
    }

    [Theory]
    [InlineData(ApplicationStatus.InReview)]
    [InlineData(ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Offered)]
    [InlineData(ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Rejected)]
    public async Task UpdateStatus_FromSubmittedToAnyNonWithdrawnStatus_Succeeds(ApplicationStatus target)
    {
        var (_, user, application) = await NewApplicationAsync();

        var updated = await _api.SetApplicationStatusAsync(application.Id, target);

        Assert.Equal(target, updated.Status);
        Assert.Equal(target, (await GetAsCandidateAsync(user, application.Id)).Status);
    }

    [Fact]
    public async Task UpdateStatus_AllowsAnyTransitionIncludingBackwards()
    {
        var (_, _, application) = await NewApplicationAsync();

        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Rejected);
        var reopened = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "Second chance");
        var hired = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Hired);

        Assert.Equal(ApplicationStatus.InReview, reopened.Status);
        Assert.Equal(
            new[] { ApplicationStatus.Submitted, ApplicationStatus.Rejected, ApplicationStatus.InReview, ApplicationStatus.Hired },
            hired.History!.Select(h => h.Status));
    }

    [Fact]
    public async Task UpdateStatus_ToWithdrawn_ReturnsBadRequest()
    {
        var (_, user, application) = await NewApplicationAsync();

        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.Withdrawn);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Only the candidate can withdraw an application", await MessageAsync(response));
        Assert.Equal(ApplicationStatus.Submitted, (await GetAsCandidateAsync(user, application.Id)).Status);
    }

    [Fact]
    public async Task UpdateStatus_OfWithdrawnApplication_ReturnsBadRequest()
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);

        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "Too late");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Application has been withdrawn by the candidate", await MessageAsync(response));
        var current = await GetAsCandidateAsync(user, application.Id);
        Assert.Equal(ApplicationStatus.Withdrawn, current.Status);
        Assert.Equal(2, current.History!.Count);
    }

    [Fact]
    public async Task UpdateStatus_NoteOnWithdrawnApplication_ReturnsBadRequest()
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);

        // ten sam status z komentarzem też jest odrzucany - aplikacja jest zamknięta dla rekrutera
        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.Withdrawn, "note");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ToSameStatusWithoutComment_ReturnsBadRequest()
    {
        var (_, _, application) = await NewApplicationAsync();

        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.Submitted);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Application already has this status - add a comment to record a note", await MessageAsync(response));
        Assert.Single((await GetAsAdminAsync(application.Id)).History!);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UpdateStatus_ToSameStatusWithBlankComment_ReturnsBadRequest(string comment)
    {
        var (_, _, application) = await NewApplicationAsync();

        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.Submitted, comment);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UpdateStatus_ToSameStatusWithComment_AddsNoteEntryWithoutChangingStatus()
    {
        var (_, user, application) = await NewApplicationAsync();
        var changed = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview);

        var noted = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview, "Interview scheduled for Monday");

        Assert.Equal(ApplicationStatus.Interview, noted.Status);
        // notatka nie zmienia daty ostatniej zmiany statusu
        Assert.Equal(changed.StatusChangedAt, noted.StatusChangedAt);
        Assert.Equal(3, noted.History!.Count);
        var note = noted.History![2];
        Assert.Equal(ApplicationStatus.Interview, note.Status);
        Assert.Equal("Interview scheduled for Monday", note.Comment);
        Assert.True(note.ChangedAt >= noted.History![1].ChangedAt);
        // notatka rekrutera jest widoczna dla kandydata
        var candidateView = await GetAsCandidateAsync(user, application.Id);
        Assert.Equal("Interview scheduled for Monday", candidateView.History![2].Comment);
    }

    [Fact]
    public async Task UpdateStatus_TrimsComment()
    {
        var (_, _, application) = await NewApplicationAsync();

        var updated = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "  padded comment \n");

        Assert.Equal("padded comment", updated.History!.Last().Comment);
    }

    [Fact]
    public async Task UpdateStatus_WithBlankCommentAndNewStatus_StoresNullComment()
    {
        var (_, _, application) = await NewApplicationAsync();

        var updated = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "   ");

        Assert.Null(updated.History!.Last().Comment);
    }

    [Fact]
    public async Task UpdateStatus_WithCommentOf1000Characters_Succeeds()
    {
        var (_, _, application) = await NewApplicationAsync();

        var updated = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, new string('x', 1000));

        Assert.Equal(1000, updated.History!.Last().Comment!.Length);
    }

    [Fact]
    public async Task UpdateStatus_WithCommentLongerThan1000Characters_ReturnsBadRequestAndKeepsApplication()
    {
        var (_, _, application) = await NewApplicationAsync();

        var response = await _api.ChangeApplicationStatusAsync(application.Id, ApplicationStatus.InReview, new string('x', 1001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("Comment", out _));
        var current = await GetAsAdminAsync(application.Id);
        Assert.Equal(ApplicationStatus.Submitted, current.Status);
        Assert.Single(current.History!);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("""{"status":null}""")]
    [InlineData("""{"status":7}""")]
    [InlineData("""{"status":-1}""")]
    [InlineData("""{"status":99}""")]
    [InlineData("""{"status":"nonsense"}""")]
    [InlineData("""{"comment":"only a comment"}""")]
    public async Task UpdateStatus_WithMissingOrInvalidStatus_ReturnsBadRequest(string json)
    {
        var (_, _, application) = await NewApplicationAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutRawJsonAsync($"{Url}/{application.Id}/status", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApplicationStatus.Submitted, (await GetAsAdminAsync(application.Id)).Status);
    }

    [Fact]
    public async Task UpdateStatus_WhenOfferIsClosed_StillWorks()
    {
        var (job, _, application) = await NewApplicationAsync();
        using var admin = await _api.AdminClientAsync();
        await admin.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });

        var updated = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Hired, "Welcome aboard");

        Assert.Equal(ApplicationStatus.Hired, updated.Status);
    }

    [Fact]
    public async Task UpdateStatus_IsVisibleToCandidateInMyApplicationsWithoutAuthors()
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Offered, "We would like to offer you the job");
        using var client = _api.CreateClient(user);

        var mine = await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        var detail = await _api.GetApplicationAsync(client, application.Id);

        Assert.Equal(ApplicationStatus.Offered, Assert.Single(mine).Status);
        Assert.Equal(new[] { ApplicationStatus.Submitted, ApplicationStatus.Offered }, detail.History!.Select(h => h.Status));
        Assert.All(detail.History!, h => Assert.Null(h.ChangedBy));
        Assert.Equal("We would like to offer you the job", detail.History![1].Comment);
    }

    // --- POST /api/jobapplications/{id}/withdraw ---

    [Fact]
    public async Task Withdraw_WithoutToken_ReturnsUnauthorized()
    {
        var (_, _, application) = await NewApplicationAsync();
        using var client = _api.CreateClient();

        var response = await client.PostAsync($"{Url}/{application.Id}/withdraw", null);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData(ApplicationStatus.Submitted)]
    [InlineData(ApplicationStatus.InReview)]
    [InlineData(ApplicationStatus.Interview)]
    [InlineData(ApplicationStatus.Offered)]
    public async Task Withdraw_FromAllowedStatus_ReturnsWithdrawnApplicationWithHistory(ApplicationStatus from)
    {
        var (_, user, application) = await NewApplicationAsync();
        if (from != ApplicationStatus.Submitted)
        {
            await _api.SetApplicationStatusAsync(application.Id, from, "Moving on");
        }

        var response = await _api.WithdrawAsync(user, application.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var withdrawn = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(application.Id, withdrawn.Id);
        Assert.Equal(ApplicationStatus.Withdrawn, withdrawn.Status);
        var last = withdrawn.History!.Last();
        Assert.Equal(ApplicationStatus.Withdrawn, last.Status);
        Assert.Null(last.Comment);
        Assert.Equal(withdrawn.StatusChangedAt, last.ChangedAt);
        // kandydat nie widzi autorów zmian
        Assert.All(withdrawn.History!, h => Assert.Null(h.ChangedBy));
    }

    [Theory]
    [InlineData(ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Rejected)]
    public async Task Withdraw_FromFinalStatus_ReturnsBadRequestAndKeepsStatus(ApplicationStatus from)
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, from);

        var response = await _api.WithdrawAsync(user, application.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Application cannot be withdrawn in its current status", await MessageAsync(response));
        var current = await GetAsCandidateAsync(user, application.Id);
        Assert.Equal(from, current.Status);
        Assert.Equal(2, current.History!.Count);
    }

    [Fact]
    public async Task Withdraw_AlreadyWithdrawn_ReturnsBadRequestWithoutExtraHistory()
    {
        var (_, user, application) = await NewApplicationAsync();
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(user, application.Id)).StatusCode);

        var response = await _api.WithdrawAsync(user, application.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(2, (await GetAsCandidateAsync(user, application.Id)).History!.Count);
    }

    [Fact]
    public async Task Withdraw_SomeoneElsesApplication_ReturnsNotFoundAndKeepsStatus()
    {
        var (_, owner, application) = await NewApplicationAsync();
        var other = await _api.RegisterUserAsync("other");

        var response = await _api.WithdrawAsync(other, application.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Application not found", await MessageAsync(response));
        Assert.Equal(ApplicationStatus.Submitted, (await GetAsCandidateAsync(owner, application.Id)).Status);
    }

    [Fact]
    public async Task Withdraw_SomeoneElsesApplicationAsAdmin_ReturnsNotFound()
    {
        var (_, owner, application) = await NewApplicationAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsync($"{Url}/{application.Id}/withdraw", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(ApplicationStatus.Submitted, (await GetAsCandidateAsync(owner, application.Id)).Status);
    }

    [Fact]
    public async Task Withdraw_NonexistentApplication_ReturnsNotFound()
    {
        var user = await _api.RegisterUserAsync();

        var response = await _api.WithdrawAsync(user, int.MaxValue);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_ByCandidate_AdminSeesCandidateAsAuthorOfEachEntry()
    {
        var (_, user, application) = await NewApplicationAsync();

        var response = await _api.WithdrawAsync(user, application.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var withdrawn = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.All(withdrawn.History!, h => Assert.Null(h.ChangedBy));

        var detail = await GetAsAdminAsync(application.Id);
        Assert.Equal(2, detail.History!.Count);
        Assert.All(detail.History, h => Assert.Contains(user.Email, h.ChangedBy));
    }

    [Fact]
    public async Task Withdraw_WhenOfferIsClosed_StillWorks()
    {
        var (job, user, application) = await NewApplicationAsync();
        using var admin = await _api.AdminClientAsync();
        await admin.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });

        var response = await _api.WithdrawAsync(user, application.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Withdraw_KeepsApplicationInMyApplicationsAndOnAdminLists()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        using var client = _api.CreateClient(user);
        using var admin = await _api.AdminClientAsync();

        var mine = await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        var byJob = await (await admin.GetAsync($"{Url}/all?pageSize=50&jobOfferId={job.Id}")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Equal(ApplicationStatus.Withdrawn, Assert.Single(mine).Status);
        Assert.Equal(ApplicationStatus.Withdrawn, Assert.Single(byJob).Status);
    }

    // --- ponowna aplikacja po wycofaniu (reaktywacja) ---

    [Fact]
    public async Task Apply_AfterWithdrawal_ReactivatesTheSameApplication()
    {
        var (job, user, application) = await NewApplicationAsync("First note");
        await _api.WithdrawAsync(user, application.Id);
        var beforeReapply = DateTime.UtcNow.AddSeconds(-5);

        var response = await TryApplyAsync(user, job.Id, "Second note");

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.EndsWith($"/api/JobApplications/{application.Id}", response.Headers.Location?.ToString());
        var reactivated = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(application.Id, reactivated.Id);
        Assert.Equal(ApplicationStatus.Submitted, reactivated.Status);
        Assert.Equal("Second note", reactivated.Note);
        Assert.True(reactivated.AppliedAt >= beforeReapply);
        Assert.True(reactivated.AppliedAt > application.AppliedAt);
        Assert.Equal(reactivated.AppliedAt, reactivated.StatusChangedAt);
        Assert.Equal(
            new[] { ApplicationStatus.Submitted, ApplicationStatus.Withdrawn, ApplicationStatus.Submitted },
            reactivated.History!.Select(h => h.Status));
        Assert.Equal(reactivated.AppliedAt, reactivated.History![2].ChangedAt);
        Assert.All(reactivated.History!, h => Assert.Null(h.ChangedBy));
    }

    [Fact]
    public async Task Apply_AfterWithdrawal_DoesNotCreateSecondApplication()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        await TryApplyAsync(user, job.Id);
        using var client = _api.CreateClient(user);
        using var admin = await _api.AdminClientAsync();

        var mine = await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        var byJob = await (await admin.GetAsync($"{Url}/all?pageSize=50&jobOfferId={job.Id}")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Equal(application.Id, Assert.Single(mine).Id);
        Assert.Single(byJob);
    }

    [Fact]
    public async Task Apply_AfterWithdrawalWithoutNote_ClearsPreviousNote()
    {
        var (job, user, application) = await NewApplicationAsync("Old note");
        await _api.WithdrawAsync(user, application.Id);

        var reactivated = await (await TryApplyAsync(user, job.Id)).ReadJsonAsync<JobApplicationDto>();

        Assert.Null(reactivated.Note);
    }

    [Fact]
    public async Task Apply_AfterWithdrawalWithTooLongNote_ReturnsBadRequestAndKeepsWithdrawal()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);

        var response = await TryApplyAsync(user, job.Id, new string('n', 1001));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(ApplicationStatus.Withdrawn, (await GetAsCandidateAsync(user, application.Id)).Status);
    }

    [Fact]
    public async Task Apply_WhileReactivatedApplicationIsActive_ReturnsBadRequest()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        Assert.Equal(HttpStatusCode.Created, (await TryApplyAsync(user, job.Id)).StatusCode);

        var response = await TryApplyAsync(user, job.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("You have already applied to this job", await MessageAsync(response));
    }

    [Fact]
    public async Task Apply_WhileApplicationIsRejected_ReturnsBadRequest()
    {
        // tylko wycofana aplikacja może zostać reaktywowana
        var (job, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Rejected);

        var response = await TryApplyAsync(user, job.Id);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("You have already applied to this job", await MessageAsync(response));
    }

    [Fact]
    public async Task Apply_WithdrawAndReapplyRepeatedly_AccumulatesHistoryOnSingleApplication()
    {
        var (job, user, application) = await NewApplicationAsync();

        for (var i = 0; i < 2; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(user, application.Id)).StatusCode);
            var response = await TryApplyAsync(user, job.Id, $"round {i}");
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            Assert.Equal(application.Id, (await response.ReadJsonAsync<JobApplicationDto>()).Id);
        }

        var final = await GetAsCandidateAsync(user, application.Id);
        Assert.Equal(
            new[]
            {
                ApplicationStatus.Submitted, ApplicationStatus.Withdrawn, ApplicationStatus.Submitted,
                ApplicationStatus.Withdrawn, ApplicationStatus.Submitted
            },
            final.History!.Select(h => h.Status));
        Assert.Equal("round 1", final.Note);
    }

    [Fact]
    public async Task Apply_AfterWithdrawal_AllowsRecruiterToChangeStatusAgain()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        await TryApplyAsync(user, job.Id);

        var updated = await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview);

        Assert.Equal(ApplicationStatus.Interview, updated.Status);
    }

    // Równoległe reaktywacje: przegrane żądania dostają 400 "already applied" (wcześniej deadlock MySQL dawał 500)
    [Fact]
    public async Task Apply_ConcurrentReactivations_RejectedRequestsReturnBadRequest()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        using var client = _api.CreateClient(user);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
    }

    // Niezależnie od kodów odpowiedzi odrzuconych żądań dane muszą pozostać spójne
    [Fact]
    public async Task Apply_ConcurrentReactivations_CreateNoDuplicatesAndExactlyOneNewHistoryEntry()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        using var client = _api.CreateClient(user);

        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
            client.PostJsonAsync(Url, new CreateJobApplicationDto { JobOfferId = job.Id })));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        var mine = await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(application.Id, Assert.Single(mine).Id);
        var current = await _api.GetApplicationAsync(client, application.Id);
        Assert.Equal(ApplicationStatus.Submitted, current.Status);
        Assert.Equal(
            new[] { ApplicationStatus.Submitted, ApplicationStatus.Withdrawn, ApplicationStatus.Submitted },
            current.History!.Select(h => h.Status));
    }

    // --- DELETE ---

    [Fact]
    public async Task Delete_ByAdmin_RemovesApplicationTogetherWithItsHistory()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "comment");
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/{application.Id}")).StatusCode);
        // kandydat może aplikować od nowa - to nowa aplikacja z czystą historią
        var fresh = await (await TryApplyAsync(user, job.Id)).ReadJsonAsync<JobApplicationDto>();
        Assert.NotEqual(application.Id, fresh.Id);
        Assert.Single(fresh.History!);
    }

    [Fact]
    public async Task Delete_WithdrawnApplicationByAdmin_ReturnsNoContent()
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.WithdrawAsync(user, application.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_ByCandidate_ReturnsForbiddenForActiveAndWithdrawnApplications()
    {
        var (_, user, application) = await NewApplicationAsync();
        using var client = _api.CreateClient(user);

        var active = await client.DeleteAsync($"{Url}/{application.Id}");
        await _api.WithdrawAsync(user, application.Id);
        var withdrawn = await client.DeleteAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, active.StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, withdrawn.StatusCode);
        Assert.Equal(ApplicationStatus.Withdrawn, (await _api.GetApplicationAsync(client, application.Id)).Status);
    }

    [Fact]
    public async Task DeleteJob_WithApplicationsAndHistory_RemovesThemTogether()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "comment");
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/{application.Id}")).StatusCode);
        using var client = _api.CreateClient(user);
        Assert.Empty(await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>());
    }

    // --- GET check/{jobId} ---

    [Fact]
    public async Task Check_ForJobWithoutApplication_ReturnsEmptyResult()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync();
        using var client = _api.CreateClient(user);

        var response = await client.GetAsync($"{Url}/check/{job.Id}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.False(body.RootElement.GetProperty("hasApplied").GetBoolean());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("applicationId").ValueKind);
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("status").ValueKind);
    }

    [Fact]
    public async Task Check_TracksApplicationThroughItsLifecycle()
    {
        var (job, user, application) = await NewApplicationAsync();
        using var client = _api.CreateClient(user);

        async Task<ApplicationCheckDto> CheckAsync() =>
            await (await client.GetAsync($"{Url}/check/{job.Id}")).ReadJsonAsync<ApplicationCheckDto>();

        var applied = await CheckAsync();
        Assert.True(applied.HasApplied);
        Assert.Equal(application.Id, applied.ApplicationId);
        Assert.Equal(ApplicationStatus.Submitted, applied.Status);

        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview);
        var interview = await CheckAsync();
        Assert.True(interview.HasApplied);
        Assert.Equal(ApplicationStatus.Interview, interview.Status);

        await _api.WithdrawAsync(user, application.Id);
        var withdrawn = await CheckAsync();
        Assert.False(withdrawn.HasApplied);
        Assert.Equal(application.Id, withdrawn.ApplicationId);
        Assert.Equal(ApplicationStatus.Withdrawn, withdrawn.Status);

        await TryApplyAsync(user, job.Id);
        var reactivated = await CheckAsync();
        Assert.True(reactivated.HasApplied);
        Assert.Equal(application.Id, reactivated.ApplicationId);
        Assert.Equal(ApplicationStatus.Submitted, reactivated.Status);
    }

    [Fact]
    public async Task Check_RejectedApplication_StillCountsAsApplied()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Rejected);
        using var client = _api.CreateClient(user);

        var check = await (await client.GetAsync($"{Url}/check/{job.Id}")).ReadJsonAsync<ApplicationCheckDto>();

        Assert.True(check.HasApplied);
        Assert.Equal(ApplicationStatus.Rejected, check.Status);
    }

    [Fact]
    public async Task Check_DoesNotLeakOtherUsersApplications()
    {
        var (job, _, application) = await NewApplicationAsync();
        var other = await _api.RegisterUserAsync("other");
        using var client = _api.CreateClient(other);

        var check = await (await client.GetAsync($"{Url}/check/{job.Id}")).ReadJsonAsync<ApplicationCheckDto>();

        Assert.False(check.HasApplied);
        Assert.Null(check.ApplicationId);
    }

    // --- GET /api/jobapplications/{id}: historia i changedBy ---

    [Fact]
    public async Task GetById_ReturnsHistoryOldestFirstWithoutAuthorsForCandidate()
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "Reviewing");
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview, "Call on Friday");
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Interview, "Moved to Monday");
        using var client = _api.CreateClient(user);

        var response = await client.GetAsync($"{Url}/{application.Id}");

        var detail = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(
            new[] { ApplicationStatus.Submitted, ApplicationStatus.InReview, ApplicationStatus.Interview, ApplicationStatus.Interview },
            detail.History!.Select(h => h.Status));
        Assert.Equal(new string?[] { null, "Reviewing", "Call on Friday", "Moved to Monday" }, detail.History!.Select(h => h.Comment));
        Assert.Equal(detail.History!.OrderBy(h => h.ChangedAt).Select(h => h.ChangedAt), detail.History!.Select(h => h.ChangedAt));
        Assert.All(detail.History!, h => Assert.Null(h.ChangedBy));
        // pole changedBy jest w JSON-ie jawnie null, a nie pominięte z odpowiedzi kandydata
        var raw = JsonDocument.Parse(await (await client.GetAsync($"{Url}/{application.Id}")).Content.ReadAsStringAsync());
        foreach (var entry in raw.RootElement.GetProperty("history").EnumerateArray())
        {
            Assert.True(entry.TryGetProperty("changedBy", out var changedBy));
            Assert.Equal(JsonValueKind.Null, changedBy.ValueKind);
        }
    }

    [Fact]
    public async Task GetById_AsAdmin_ReturnsAuthorsOfEachChange()
    {
        var (_, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview);
        await _api.WithdrawAsync(user, application.Id);

        var detail = await GetAsAdminAsync(application.Id);

        Assert.Equal(3, detail.History!.Count);
        Assert.Equal($"Test User ({user.Email})", detail.History![0].ChangedBy);
        Assert.Contains(TestUsers.AdminEmail, detail.History![1].ChangedBy);
        Assert.Equal($"Test User ({user.Email})", detail.History![2].ChangedBy);
    }

    // --- listy ---

    [Fact]
    public async Task Lists_IncludeStatusAndStatusChangedAtButNoHistory()
    {
        var (job, user, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.InReview, "comment");
        using var client = _api.CreateClient(user);
        using var admin = await _api.AdminClientAsync();

        var responses = new[]
        {
            await client.GetAsync($"{Url}/my-applications"),
            await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}")
        };

        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var item = Assert.Single(body.RootElement.EnumerateArray(), e => e.GetProperty("id").GetInt32() == application.Id);
            Assert.Equal(1, item.GetProperty("status").GetInt32());
            Assert.True(item.GetProperty("statusChangedAt").GetDateTime() > application.AppliedAt);
            Assert.EndsWith("Z", item.GetProperty("statusChangedAt").GetString());
            Assert.True(item.TryGetProperty("history", out var history));
            Assert.Equal(JsonValueKind.Null, history.ValueKind);
        }
    }

    [Fact]
    public async Task All_FilteredByStatus_ReturnsOnlyApplicationsWithThatStatus()
    {
        var job = await _api.CreateJobAsync();
        var users = new List<TestUser>();
        var applications = new List<JobApplicationDto>();
        for (var i = 0; i < 4; i++)
        {
            var user = await _api.RegisterUserAsync("filter");
            users.Add(user);
            applications.Add(await _api.ApplyAsync(user, job.Id));
        }

        await _api.SetApplicationStatusAsync(applications[1].Id, ApplicationStatus.Interview);
        await _api.SetApplicationStatusAsync(applications[2].Id, ApplicationStatus.Interview);
        await _api.WithdrawAsync(users[3], applications[3].Id);
        using var admin = await _api.AdminClientAsync();

        async Task<(int[] Ids, int Total)> QueryAsync(string status)
        {
            var response = await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&status={status}");
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var items = await response.ReadJsonAsync<JobApplicationDto[]>();
            return (items.Select(a => a.Id).Order().ToArray(), response.ReadPagination().TotalItems);
        }

        var submitted = await QueryAsync("0");
        var interview = await QueryAsync("2");
        var withdrawn = await QueryAsync("6");
        var hired = await QueryAsync("4");

        Assert.Equal(new[] { applications[0].Id }, submitted.Ids);
        Assert.Equal(new[] { applications[1].Id, applications[2].Id }.Order(), interview.Ids);
        Assert.Equal(2, interview.Total);
        Assert.Equal(new[] { applications[3].Id }, withdrawn.Ids);
        Assert.Empty(hired.Ids);
        Assert.Equal(0, hired.Total);
    }

    [Fact]
    public async Task All_FilteredByStatusName_IsAccepted()
    {
        var (job, _, application) = await NewApplicationAsync();
        await _api.SetApplicationStatusAsync(application.Id, ApplicationStatus.Offered);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&status=Offered");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(application.Id, Assert.Single(await response.ReadJsonAsync<JobApplicationDto[]>()).Id);
    }

    [Fact]
    public async Task All_WithoutStatus_ReturnsApplicationsInEveryStatus()
    {
        var (job, user, application) = await NewApplicationAsync();
        var second = await _api.ApplyAsync(await _api.RegisterUserAsync(), job.Id);
        await _api.WithdrawAsync(user, application.Id);
        using var admin = await _api.AdminClientAsync();

        var items = await (await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Equal(new[] { application.Id, second.Id }.Order(), items.Select(a => a.Id).Order());
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("7")]
    [InlineData("foo")]
    public async Task All_WithInvalidStatus_ReturnsBadRequest(string status)
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?status={status}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task All_StatusFilterCombinesWithSearchTerm()
    {
        var job = await _api.CreateJobAsync();
        var first = await _api.RegisterUserAsync("combo");
        var second = await _api.RegisterUserAsync("combo");
        var firstApp = await _api.ApplyAsync(first, job.Id);
        await _api.ApplyAsync(second, job.Id);
        await _api.SetApplicationStatusAsync(firstApp.Id, ApplicationStatus.Rejected);
        using var admin = await _api.AdminClientAsync();

        var items = await (await admin.GetAsync($"{Url}/all?status=5&searchTerm={Uri.EscapeDataString(first.Email)}&jobOfferId={job.Id}"))
            .ReadJsonAsync<JobApplicationDto[]>();
        var none = await (await admin.GetAsync($"{Url}/all?status=5&searchTerm={Uri.EscapeDataString(second.Email)}&jobOfferId={job.Id}"))
            .ReadJsonAsync<JobApplicationDto[]>();

        Assert.Equal(firstApp.Id, Assert.Single(items).Id);
        Assert.Empty(none);
    }
}
