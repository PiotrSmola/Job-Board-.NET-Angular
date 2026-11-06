using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

[Collection(ApiCollection.Name)]
public class JobApplicationsTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;

    public JobApplicationsTests(ApiFixture api) => _api = api;

    private static CreateJobApplicationDto Apply(int jobId, string? note = null) =>
        new() { JobOfferId = jobId, Note = note };

    // --- składanie aplikacji ---

    [Fact]
    public async Task Apply_WithoutToken_ReturnsUnauthorized()
    {
        var job = await _api.CreateJobAsync();
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync(Url, Apply(job.Id));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Apply_ToExistingJob_ReturnsCreatedWithFullyPopulatedDto()
    {
        var job = await _api.CreateJobAsync(j => { j.Title = "Apply target"; j.CompanyId = _api.CompanyId("Apply company"); });
        var user = await _api.RegisterUserAsync("applicant");
        using var client = _api.CreateClient(user);

        var response = await client.PostJsonAsync(Url, Apply(job.Id, "My motivation"));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var application = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.True(application.Id > 0);
        Assert.EndsWith($"/api/JobApplications/{application.Id}", response.Headers.Location?.ToString());
        Assert.Equal(job.Id, application.JobOfferId);
        Assert.Equal(user.Id, application.UserId);
        Assert.Equal("My motivation", application.Note);
        Assert.Equal(user.Email, application.UserEmail);
        Assert.Equal("Test User", application.UserFullName);
        Assert.Equal("Apply target", application.JobTitle);
        Assert.Equal("Apply company", application.Company);
        Assert.True(Math.Abs((DateTime.UtcNow - application.AppliedAt).TotalMinutes) < 5);
        Assert.NotNull(application.JobOffer);
        Assert.Equal(job.Id, application.JobOffer!.Id);
        Assert.Equal("Apply target", application.JobOffer.Title);
    }

    [Fact]
    public async Task Apply_WithoutNote_Succeeds()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("nonote");
        using var client = _api.CreateClient(user);

        var response = await client.PostJsonAsync(Url, Apply(job.Id));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await response.ReadJsonAsync<JobApplicationDto>()).Note);
    }

    [Fact]
    public async Task Apply_Twice_ToSameJob_ReturnsBadRequest()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("twice");
        await _api.ApplyAsync(user, job.Id);
        using var client = _api.CreateClient(user);

        var response = await client.PostJsonAsync(Url, Apply(job.Id));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("You have already applied to this job", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Apply_ConcurrentRequestsToSameJob_CreateExactlyOneApplication()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("concurrent");
        using var client = _api.CreateClient(user);

        // równoległe żądania mogą wszystkie przejść sprawdzenie HasUserApplied - decyduje unikalny indeks
        var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => client.PostJsonAsync(Url, Apply(job.Id))));

        Assert.Single(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.Created), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        foreach (var rejected in responses.Where(r => r.StatusCode == HttpStatusCode.BadRequest))
        {
            using var body = JsonDocument.Parse(await rejected.Content.ReadAsStringAsync());
            Assert.Equal("You have already applied to this job", body.RootElement.GetProperty("message").GetString());
        }

        var mine = await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Single(mine);
    }

    [Fact]
    public async Task Apply_TwoDifferentUsers_ToSameJob_BothSucceed()
    {
        var job = await _api.CreateJobAsync();
        var first = await _api.RegisterUserAsync("first");
        var second = await _api.RegisterUserAsync("second");

        await _api.ApplyAsync(first, job.Id);
        await _api.ApplyAsync(second, job.Id);
    }

    [Fact]
    public async Task Apply_ToNonexistentJob_ReturnsNotFound()
    {
        var user = await _api.RegisterUserAsync("nojob");
        using var client = _api.CreateClient(user);

        var response = await client.PostJsonAsync(Url, Apply(999999999));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Job offer not found", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task Apply_WithNoteLongerThan1000Characters_ReturnsBadRequest()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("longnote");
        using var client = _api.CreateClient(user);

        var response = await client.PostJsonAsync(Url, Apply(job.Id, new string('n', 1001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- pojedyncza aplikacja ---

    [Fact]
    public async Task GetById_LocationFromApply_ReturnsTheCreatedApplicationToOwner()
    {
        var job = await _api.CreateJobAsync(j => j.Title = "Get by id");
        var user = await _api.RegisterUserAsync("getbyid");
        using var client = _api.CreateClient(user);
        var created = await client.PostJsonAsync(Url, Apply(job.Id, "note"));
        var createdDto = await created.ReadJsonAsync<JobApplicationDto>();

        var response = await client.GetAsync(created.Headers.Location);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var application = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal(createdDto.Id, application.Id);
        Assert.Equal(user.Id, application.UserId);
        Assert.Equal("note", application.Note);
        Assert.Equal("Get by id", application.JobOffer!.Title);
    }

    [Fact]
    public async Task GetById_SomeoneElsesApplicationAsUser_ReturnsNotFound()
    {
        var job = await _api.CreateJobAsync();
        var owner = await _api.RegisterUserAsync("getowner");
        var intruder = await _api.RegisterUserAsync("getintruder");
        var application = await _api.ApplyAsync(owner, job.Id);
        using var intruderClient = _api.CreateClient(intruder);

        var response = await intruderClient.GetAsync($"{Url}/{application.Id}");

        // tak samo jak dla nieistniejącej aplikacji - bez ujawniania, że istnieje
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Application not found", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetById_SomeoneElsesApplicationAsAdmin_ReturnsOk()
    {
        var job = await _api.CreateJobAsync();
        var owner = await _api.RegisterUserAsync("getadmin");
        var application = await _api.ApplyAsync(owner, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(owner.Email, (await response.ReadJsonAsync<JobApplicationDto>()).UserEmail);
    }

    [Fact]
    public async Task GetById_NonexistentApplication_ReturnsNotFound()
    {
        var user = await _api.RegisterUserAsync("getmissing");
        using var client = _api.CreateClient(user);

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync($"{Url}/999999999")).StatusCode);
    }

    [Fact]
    public async Task GetById_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Url}/1")).StatusCode);
    }

    // --- moje aplikacje ---

    [Fact]
    public async Task MyApplications_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"{Url}/my-applications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task MyApplications_ForNewUser_ReturnsEmptyList()
    {
        var user = await _api.RegisterUserAsync("empty");
        using var client = _api.CreateClient(user);

        var response = await client.GetAsync($"{Url}/my-applications");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadJsonAsync<JobApplicationDto[]>());
    }

    [Fact]
    public async Task MyApplications_ReturnsOnlyOwnApplicationsNewestFirstWithJobOffer()
    {
        var jobA = await _api.CreateJobAsync(j => j.Title = "Job A");
        var jobB = await _api.CreateJobAsync(j => j.Title = "Job B");
        var me = await _api.RegisterUserAsync("mine");
        var someoneElse = await _api.RegisterUserAsync("notmine");
        var first = await _api.ApplyAsync(me, jobA.Id);
        var second = await _api.ApplyAsync(me, jobB.Id);
        await _api.ApplyAsync(someoneElse, jobA.Id);
        using var client = _api.CreateClient(me);

        var response = await client.GetAsync($"{Url}/my-applications");

        var applications = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(new[] { second.Id, first.Id }, applications.Select(a => a.Id).ToArray());
        Assert.All(applications, a =>
        {
            Assert.Equal(me.Id, a.UserId);
            Assert.NotNull(a.JobOffer);
            Assert.Equal(a.JobOfferId, a.JobOffer!.Id);
        });
        Assert.Equal("Job B", applications[0].JobTitle);
        Assert.Equal("Job A", applications[1].JobOffer!.Title);
    }

    // --- sprawdzenie, czy aplikowano ---

    [Fact]
    public async Task Check_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"{Url}/check/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Check_ReflectsWhetherCurrentUserApplied()
    {
        var job = await _api.CreateJobAsync();
        var applicant = await _api.RegisterUserAsync("checker");
        var other = await _api.RegisterUserAsync("checkother");
        using var applicantClient = _api.CreateClient(applicant);
        using var otherClient = _api.CreateClient(other);

        Assert.False(await HasAppliedAsync(applicantClient, job.Id));

        await _api.ApplyAsync(applicant, job.Id);

        Assert.True(await HasAppliedAsync(applicantClient, job.Id));
        Assert.False(await HasAppliedAsync(otherClient, job.Id));
    }

    [Fact]
    public async Task Check_ForNonexistentJob_ReturnsFalse()
    {
        var user = await _api.RegisterUserAsync("checknojob");
        using var client = _api.CreateClient(user);

        Assert.False(await HasAppliedAsync(client, 999999999));
    }

    private static async Task<bool> HasAppliedAsync(HttpClient client, int jobId)
    {
        var response = await client.GetAsync($"{Url}/check/{jobId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("hasApplied").GetBoolean();
    }

    // --- usuwanie ---

    [Fact]
    public async Task Delete_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        var response = await client.DeleteAsync($"{Url}/1");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OwnApplicationAsCandidate_ReturnsForbiddenAndKeepsApplication()
    {
        // kandydat nie usuwa aplikacji - wycofuje ją (POST withdraw); usuwanie jest tylko dla admina
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("deleteown");
        var application = await _api.ApplyAsync(user, job.Id);
        using var client = _api.CreateClient(user);

        var response = await client.DeleteAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Single(await (await client.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>());
        Assert.True(await HasAppliedAsync(client, job.Id));
    }

    [Fact]
    public async Task Delete_ByAdmin_AllowsCandidateToApplyAgain()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("reapply");
        var application = await _api.ApplyAsync(user, job.Id);
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{application.Id}")).StatusCode);
        using var client = _api.CreateClient(user);

        var again = await client.PostJsonAsync(Url, Apply(job.Id));

        Assert.Equal(HttpStatusCode.Created, again.StatusCode);
    }

    [Fact]
    public async Task Delete_SomeoneElsesApplicationAsUser_ReturnsForbiddenAndKeepsApplication()
    {
        var job = await _api.CreateJobAsync();
        var owner = await _api.RegisterUserAsync("owner");
        var intruder = await _api.RegisterUserAsync("intruder");
        var application = await _api.ApplyAsync(owner, job.Id);
        using var intruderClient = _api.CreateClient(intruder);
        using var ownerClient = _api.CreateClient(owner);

        var response = await intruderClient.DeleteAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await HasAppliedAsync(ownerClient, job.Id));
    }

    [Fact]
    public async Task Delete_SomeoneElsesApplicationAsAdmin_ReturnsNoContent()
    {
        var job = await _api.CreateJobAsync();
        var owner = await _api.RegisterUserAsync("adminDeleted");
        var application = await _api.ApplyAsync(owner, job.Id);
        using var admin = await _api.AdminClientAsync();
        using var ownerClient = _api.CreateClient(owner);

        var response = await admin.DeleteAsync($"{Url}/{application.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await HasAppliedAsync(ownerClient, job.Id));
    }

    [Fact]
    public async Task Delete_NonexistentApplicationAsAdmin_ReturnsNotFound()
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.DeleteAsync($"{Url}/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- lista wszystkich aplikacji (admin) ---

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
    public async Task All_AsAdmin_ReturnsPaginatedApplicationsWithHeader()
    {
        var job = await _api.CreateJobAsync();
        var user = await _api.RegisterUserAsync("allone");
        await _api.ApplyAsync(user, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var pagination = response.ReadPagination();
        Assert.Equal(1, pagination.CurrentPage);
        Assert.Equal(10, pagination.ItemsPerPage);
        Assert.True(pagination.TotalItems >= 1);
        var items = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.NotEmpty(items);
        Assert.All(items, a =>
        {
            Assert.NotNull(a.JobOffer);
            Assert.False(string.IsNullOrEmpty(a.UserEmail));
            Assert.False(string.IsNullOrEmpty(a.JobTitle));
        });
    }

    [Fact]
    public async Task All_FilteredByJobOfferId_ReturnsOnlyThatOffersApplications()
    {
        var jobA = await _api.CreateJobAsync();
        var jobB = await _api.CreateJobAsync();
        var u1 = await _api.RegisterUserAsync("filtera");
        var u2 = await _api.RegisterUserAsync("filterb");
        var a1 = await _api.ApplyAsync(u1, jobA.Id);
        var a2 = await _api.ApplyAsync(u2, jobA.Id);
        await _api.ApplyAsync(u1, jobB.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?jobOfferId={jobA.Id}");

        var items = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(new[] { a1.Id, a2.Id }.OrderBy(i => i), items.Select(a => a.Id).OrderBy(i => i));
        Assert.Equal(2, response.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task All_OrderBy_NewestIsDefaultAndOldestReverses()
    {
        var job = await _api.CreateJobAsync();
        var u1 = await _api.RegisterUserAsync("order1");
        var u2 = await _api.RegisterUserAsync("order2");
        var u3 = await _api.RegisterUserAsync("order3");
        var a1 = await _api.ApplyAsync(u1, job.Id);
        var a2 = await _api.ApplyAsync(u2, job.Id);
        var a3 = await _api.ApplyAsync(u3, job.Id);
        using var admin = await _api.AdminClientAsync();

        var defaultOrder = await (await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}")).ReadJsonAsync<JobApplicationDto[]>();
        var newest = await (await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&orderBy=newest")).ReadJsonAsync<JobApplicationDto[]>();
        var oldest = await (await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&orderBy=oldest")).ReadJsonAsync<JobApplicationDto[]>();

        Assert.Equal(new[] { a3.Id, a2.Id, a1.Id }, defaultOrder.Select(a => a.Id).ToArray());
        Assert.Equal(new[] { a3.Id, a2.Id, a1.Id }, newest.Select(a => a.Id).ToArray());
        Assert.Equal(new[] { a1.Id, a2.Id, a3.Id }, oldest.Select(a => a.Id).ToArray());
    }

    [Fact]
    public async Task All_SearchTerm_MatchesCandidateEmailNameJobTitleAndCompany()
    {
        var token = Guid.NewGuid().ToString("N");
        var job = await _api.CreateJobAsync(j => { j.Title = $"Title {token}"; j.CompanyId = _api.CompanyId($"Company {token}"); });
        var otherJob = await _api.CreateJobAsync(j => j.Title = "Other title");
        var matchingUser = await _api.RegisterUserAsync($"search{token}");
        var unrelated = await _api.RegisterUserAsync("unrelated");
        var byUserEmail = await _api.ApplyAsync(matchingUser, otherJob.Id);
        var byJob = await _api.ApplyAsync(unrelated, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?searchTerm={token.ToUpperInvariant()}&orderBy=oldest");

        var items = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(new[] { byUserEmail.Id, byJob.Id }, items.Select(a => a.Id).ToArray());
    }

    [Fact]
    public async Task All_SearchTerm_MatchesFullCandidateName()
    {
        var job = await _api.CreateJobAsync();
        var token = Guid.NewGuid().ToString("N")[..10];
        var email = $"fullname_{Guid.NewGuid():N}@test.local";
        using var anonymous = _api.CreateClient();
        var register = await anonymous.PostJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = email, Password = "Passw0rd1", FirstName = $"Zenobia{token}", LastName = $"Kowalczyk{token}"
        });
        var registered = await register.ReadJsonAsync<UserDto>();
        var user = new TestUser(email, "Passw0rd1", "Z", "K", registered.Token, JwtTestHelper.GetUserId(registered.Token));
        var application = await _api.ApplyAsync(user, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?searchTerm=Zenobia{token}%20Kowalczyk{token}");

        var items = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(new[] { application.Id }, items.Select(a => a.Id).ToArray());
        Assert.Equal($"Zenobia{token} Kowalczyk{token}", items[0].UserFullName);
    }

    [Fact]
    public async Task All_Pagination_SplitsResultsAndClampsPageSize()
    {
        var job = await _api.CreateJobAsync();
        var ids = new List<int>();
        for (var i = 0; i < 5; i++)
        {
            var user = await _api.RegisterUserAsync($"page{i}");
            ids.Add((await _api.ApplyAsync(user, job.Id)).Id);
        }

        using var admin = await _api.AdminClientAsync();

        var page1 = await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&pageSize=2&pageNumber=1");
        var page3 = await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&pageSize=2&pageNumber=3");
        var huge = await admin.GetAsync($"{Url}/all?jobOfferId={job.Id}&pageSize=1000");

        var header1 = page1.ReadPagination();
        Assert.Equal(5, header1.TotalItems);
        Assert.Equal(3, header1.TotalPages);
        Assert.Equal(2, header1.ItemsPerPage);
        Assert.Equal(2, (await page1.ReadJsonAsync<JobApplicationDto[]>()).Length);
        Assert.Equal(3, page3.ReadPagination().CurrentPage);
        Assert.Single(await page3.ReadJsonAsync<JobApplicationDto[]>());
        Assert.Equal(50, huge.ReadPagination().ItemsPerPage);
    }

    // --- aplikacje do oferty: /all?jobOfferId= (osobny endpoint /job/{jobId} usunięto - frontend go nie używał) ---

    [Fact]
    public async Task AllByJob_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync($"{Url}/all?jobOfferId=1")).StatusCode);
    }

    [Fact]
    public async Task AllByJob_AsRegularUser_ReturnsForbidden()
    {
        var job = await _api.CreateJobAsync();
        using var client = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync($"{Url}/all?pageSize=50&jobOfferId={job.Id}")).StatusCode);
    }

    [Fact]
    public async Task AllByJob_AsAdmin_ReturnsApplicationsWithJobOffer()
    {
        var job = await _api.CreateJobAsync(j => j.Title = "ByJob title");
        var u1 = await _api.RegisterUserAsync("byjob1");
        var u2 = await _api.RegisterUserAsync("byjob2");
        var first = await _api.ApplyAsync(u1, job.Id);
        var second = await _api.ApplyAsync(u2, job.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?pageSize=50&jobOfferId={job.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var items = await response.ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(new[] { second.Id, first.Id }, items.Select(a => a.Id).ToArray());
        Assert.All(items, a =>
        {
            Assert.NotNull(a.JobOffer);
            Assert.Equal("ByJob title", a.JobOffer!.Title);
            Assert.Equal(job.Id, a.JobOfferId);
        });
    }

    [Fact]
    public async Task AllByJob_ForJobWithoutApplications_ReturnsEmptyList()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?pageSize=50&jobOfferId={job.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadJsonAsync<JobApplicationDto[]>());
    }

    [Fact]
    public async Task AllByJob_ForUnknownJob_ReturnsEmptyList()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/all?jobOfferId=999999999");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadJsonAsync<JobApplicationDto[]>());
    }

    [Fact]
    public async Task RemovedByJobEndpoint_ReturnsNotFound()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"{Url}/job/{job.Id}")).StatusCode);
    }
}
