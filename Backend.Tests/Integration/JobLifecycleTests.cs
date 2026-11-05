using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Cykl życia oferty: widoczność (draft/active/expired/closed), lista dla admina, walidacja ExpiresAt
// i szybka zmiana statusu. Dane seeda są współdzielone, więc testy tworzą oferty z unikalnym tokenem w opisie
// i zawężają wyniki przez searchTerm.
[Collection(ApiCollection.Name)]
public class JobLifecycleTests
{
    private readonly ApiFixture _api;

    public JobLifecycleTests(ApiFixture api) => _api = api;

    private static string NewToken() => Guid.NewGuid().ToString("N");

    private async Task<JobOfferDto[]> PublicJobsAsync(string token)
    {
        using var client = _api.CreateClient();
        var response = await client.GetAsync($"/api/jobs?searchTerm={token}&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobOfferDto[]>();
    }

    private async Task<(HttpResponseMessage Response, JobOfferDto[] Items)> ManagedJobsAsync(string query)
    {
        using var admin = await _api.AdminClientAsync();
        var response = await admin.GetAsync("/api/jobs/manage" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, await response.ReadJsonAsync<JobOfferDto[]>());
    }

    private static async Task<JsonElement> ErrorsAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors").Clone();
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString();
    }

    private async Task<HttpResponseMessage> PatchStatusAsync(int jobId, JobOfferStatus status)
    {
        using var admin = await _api.AdminClientAsync();
        return await admin.PatchJsonAsync($"/api/jobs/{jobId}/status", new UpdateJobOfferStatusDto { Status = status });
    }

    // Po jednej ofercie w każdym stanie, wszystkie z tym samym tokenem
    private async Task<(string Token, JobOfferDto Draft, JobOfferDto ActiveOpenEnded, JobOfferDto ActiveWithDate, JobOfferDto Expired, JobOfferDto Closed)> CreateOneOfEachStateAsync()
    {
        var token = NewToken();
        void Tag(CreateJobOfferDto j) => j.Description = $"lifecycle {token}";

        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, null, Tag);
        var open = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, null, Tag);
        var dated = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(10), Tag);
        var expired = await _api.CreateExpiredJobAsync(Tag);
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, DateTime.UtcNow.AddDays(10), Tag);
        return (token, draft, open, dated, expired, closed);
    }

    // --- publiczna lista ---

    [Fact]
    public async Task GetJobs_ReturnsOnlyActiveNotExpiredOffers()
    {
        var (token, draft, open, dated, expired, closed) = await CreateOneOfEachStateAsync();

        var items = await PublicJobsAsync(token);

        Assert.Equal(new[] { open.Id, dated.Id }.Order(), items.Select(j => j.Id).Order());
        Assert.DoesNotContain(items, j => j.Id == draft.Id || j.Id == closed.Id || j.Id == expired.Id);
        Assert.All(items, j =>
        {
            Assert.Equal(JobOfferStatus.Active, j.Status);
            Assert.False(j.IsExpired);
        });
    }

    [Fact]
    public async Task GetJobs_PaginationHeaderCountsOnlyVisibleOffers()
    {
        var (token, _, _, _, _, _) = await CreateOneOfEachStateAsync();
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs?searchTerm={token}");

        Assert.Equal(2, response.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task GetJobs_OfferThatExpiresAfterCreation_DisappearsFromPublicListButStaysReadable()
    {
        var token = NewToken();
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(5), j => j.Description = $"lifecycle {token}");
        Assert.Single(await PublicJobsAsync(token));

        await _api.SetJobExpiresAtAsync(job.Id, DateTime.UtcNow.AddMinutes(-1));

        Assert.Empty(await PublicJobsAsync(token));
        using var anonymous = _api.CreateClient();
        var fetched = await (await anonymous.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.True(fetched.IsExpired);
        Assert.Equal(JobOfferStatus.Active, fetched.Status);
    }

    [Theory]
    [InlineData("CloudNative Labs", "draft")]
    [InlineData("AppFactory", "closed")]
    [InlineData("TestHouse", "expired")]
    public async Task Seed_ContainsHiddenOfferInEachNonPublicState(string company, string state)
    {
        // oferty przykładowe nie są widoczne publicznie ...
        using var client = _api.CreateClient();
        var publicItems = await (await client.GetAsync($"/api/jobs?searchTerm={Uri.EscapeDataString(company)}&pageSize=50")).ReadJsonAsync<JobOfferDto[]>();
        Assert.DoesNotContain(publicItems, j => j.Company == company);

        // ... ale admin widzi je w odpowiednim stanie
        var (_, managed) = await ManagedJobsAsync($"?state={state}&searchTerm={Uri.EscapeDataString(company)}&pageSize=50");
        Assert.Contains(managed, j => j.Company == company);
    }

    [Fact]
    public async Task Seed_PublicOffersHaveFutureExpirationDates()
    {
        // seed jest najstarszy, więc przy sortowaniu "oldest" 20 publicznych ofert z seeda wypada na początku
        var (_, managed) = await ManagedJobsAsync("?state=active&orderBy=oldest&pageSize=20");

        Assert.Equal(20, managed.Length);
        Assert.All(managed, j =>
        {
            Assert.Equal(JobOfferStatus.Active, j.Status);
            Assert.NotNull(j.ExpiresAt);
            Assert.InRange(j.ExpiresAt!.Value, DateTime.UtcNow.AddDays(13), DateTime.UtcNow.AddDays(61));
        });
    }

    // --- GET /api/jobs/manage ---

    [Fact]
    public async Task Manage_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobs/manage");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Manage_AsRegularUser_ReturnsForbidden()
    {
        using var client = await _api.UserClientAsync();

        var response = await client.GetAsync("/api/jobs/manage");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Manage_WithoutState_ReturnsOffersInEveryState()
    {
        var (token, draft, open, dated, expired, closed) = await CreateOneOfEachStateAsync();

        var (response, items) = await ManagedJobsAsync($"?searchTerm={token}");

        Assert.Equal(new[] { draft.Id, open.Id, dated.Id, expired.Id, closed.Id }.Order(), items.Select(j => j.Id).Order());
        Assert.Equal(5, response.ReadPagination().TotalItems);
    }

    [Theory]
    [InlineData("draft")]
    [InlineData("active")]
    [InlineData("expired")]
    [InlineData("closed")]
    public async Task Manage_WithState_ReturnsOnlyOffersInThatState(string state)
    {
        var (token, draft, open, dated, expired, closed) = await CreateOneOfEachStateAsync();
        var expectedIds = state switch
        {
            "draft" => new[] { draft.Id },
            "active" => new[] { open.Id, dated.Id },
            "expired" => new[] { expired.Id },
            _ => new[] { closed.Id }
        };

        var (response, items) = await ManagedJobsAsync($"?state={state}&searchTerm={token}");

        Assert.Equal(expectedIds.Order(), items.Select(j => j.Id).Order());
        Assert.Equal(expectedIds.Length, response.ReadPagination().TotalItems);
    }

    [Theory]
    [InlineData("DRAFT")]
    [InlineData("Expired")]
    public async Task Manage_StateIsCaseInsensitive(string state)
    {
        var (token, draft, _, _, expired, _) = await CreateOneOfEachStateAsync();

        var (_, items) = await ManagedJobsAsync($"?state={state}&searchTerm={token}");

        var single = Assert.Single(items);
        Assert.Equal(state.Equals("draft", StringComparison.OrdinalIgnoreCase) ? draft.Id : expired.Id, single.Id);
    }

    [Fact]
    public async Task Manage_WithEmptyState_ReturnsAllOffers()
    {
        var (token, _, _, _, _, _) = await CreateOneOfEachStateAsync();

        var (_, items) = await ManagedJobsAsync($"?state=&searchTerm={token}");

        Assert.Equal(5, items.Length);
    }

    [Theory]
    [InlineData("foo")]
    [InlineData("published")]
    [InlineData("0")]
    [InlineData("draft,closed")]
    public async Task Manage_WithUnknownState_ReturnsBadRequest(string state)
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"/api/jobs/manage?state={Uri.EscapeDataString(state)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Equal("State must be one of: draft, active, expired, closed", errors.GetProperty("State")[0].GetString());
    }

    [Fact]
    public async Task Manage_ReportsStatusExpiresAtAndIsExpiredForEachOffer()
    {
        var (token, draft, open, dated, expired, closed) = await CreateOneOfEachStateAsync();

        var (_, items) = await ManagedJobsAsync($"?searchTerm={token}");
        var byId = items.ToDictionary(j => j.Id);

        Assert.Equal(JobOfferStatus.Draft, byId[draft.Id].Status);
        Assert.False(byId[draft.Id].IsExpired);
        Assert.Equal(JobOfferStatus.Active, byId[open.Id].Status);
        Assert.Null(byId[open.Id].ExpiresAt);
        Assert.False(byId[open.Id].IsExpired);
        Assert.Equal(JobOfferStatus.Active, byId[dated.Id].Status);
        Assert.NotNull(byId[dated.Id].ExpiresAt);
        Assert.False(byId[dated.Id].IsExpired);
        Assert.Equal(JobOfferStatus.Active, byId[expired.Id].Status);
        Assert.True(byId[expired.Id].IsExpired);
        Assert.Equal(JobOfferStatus.Closed, byId[closed.Id].Status);
        // zamknięta oferta z datą w przyszłości nie jest ani aktywna, ani wygasła
        Assert.False(byId[closed.Id].IsExpired);
    }

    [Fact]
    public async Task Manage_ClosedOfferWithPastDate_IsClosedNotExpired()
    {
        var token = NewToken();
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, DateTime.UtcNow.AddDays(-3), j => j.Description = $"lifecycle {token}");

        var (_, closedItems) = await ManagedJobsAsync($"?state=closed&searchTerm={token}");
        var (_, expiredItems) = await ManagedJobsAsync($"?state=expired&searchTerm={token}");

        Assert.Equal(closed.Id, Assert.Single(closedItems).Id);
        Assert.False(closedItems[0].IsExpired);
        Assert.Empty(expiredItems);
    }

    [Fact]
    public async Task Manage_SupportsPaginationAndOtherFiltersTogetherWithState()
    {
        var token = NewToken();
        for (var i = 0; i < 3; i++)
        {
            await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, null, j => { j.Description = $"lifecycle {token}"; j.Title = $"Draft {i}"; });
        }

        var (response, items) = await ManagedJobsAsync($"?state=draft&searchTerm={token}&pageSize=2&pageNumber=2&orderBy=oldest");

        var pagination = response.ReadPagination();
        Assert.Equal(2, pagination.CurrentPage);
        Assert.Equal(2, pagination.ItemsPerPage);
        Assert.Equal(3, pagination.TotalItems);
        Assert.Equal(2, pagination.TotalPages);
        Assert.Equal("Draft 2", Assert.Single(items).Title);
    }

    // --- GET /api/jobs/{id} ---

    [Fact]
    public async Task GetById_Draft_ReturnsNotFoundForAnonymousAndRegularUser()
    {
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft);
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();

        var anonymousResponse = await anonymous.GetAsync($"/api/jobs/{draft.Id}");
        var userResponse = await user.GetAsync($"/api/jobs/{draft.Id}");

        Assert.Equal(HttpStatusCode.NotFound, anonymousResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, userResponse.StatusCode);
        Assert.Equal("Job offer not found", await MessageAsync(userResponse));
    }

    [Fact]
    public async Task GetById_Draft_ReturnsOkForAdmin()
    {
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"/api/jobs/{draft.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(JobOfferStatus.Draft, fetched.Status);
    }

    [Fact]
    public async Task GetById_ClosedOffer_ReturnsOkForEveryone()
    {
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed);
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();
        using var admin = await _api.AdminClientAsync();

        foreach (var client in new[] { anonymous, user, admin })
        {
            var response = await client.GetAsync($"/api/jobs/{closed.Id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var fetched = await response.ReadJsonAsync<JobOfferDto>();
            Assert.Equal(JobOfferStatus.Closed, fetched.Status);
            Assert.False(fetched.IsExpired);
        }
    }

    [Fact]
    public async Task GetById_ExpiredOffer_ReturnsOkForEveryoneWithIsExpired()
    {
        var expired = await _api.CreateExpiredJobAsync();
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();
        using var admin = await _api.AdminClientAsync();

        foreach (var client in new[] { anonymous, user, admin })
        {
            var response = await client.GetAsync($"/api/jobs/{expired.Id}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var fetched = await response.ReadJsonAsync<JobOfferDto>();
            Assert.Equal(JobOfferStatus.Active, fetched.Status);
            Assert.True(fetched.IsExpired);
            Assert.True(fetched.ExpiresAt < DateTime.UtcNow);
        }
    }

    [Fact]
    public async Task GetById_SerializesLifecycleFieldsInCamelCase()
    {
        var expiresAt = DateTime.UtcNow.AddDays(3);
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, expiresAt);
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{job.Id}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        // enum jako liczba, data z sufiksem Z (UTC), isExpired jako bool
        Assert.Equal(JsonValueKind.Number, root.GetProperty("status").ValueKind);
        Assert.Equal(1, root.GetProperty("status").GetInt32());
        Assert.False(root.GetProperty("isExpired").GetBoolean());
        Assert.EndsWith("Z", root.GetProperty("expiresAt").GetString());
        Assert.True(Math.Abs((root.GetProperty("expiresAt").GetDateTime().ToUniversalTime() - expiresAt).TotalSeconds) < 1);
    }

    [Fact]
    public async Task GetById_OpenEndedOffer_HasNullExpiresAt()
    {
        var job = await _api.CreateJobAsync();
        using var client = _api.CreateClient();

        using var body = JsonDocument.Parse(await (await client.GetAsync($"/api/jobs/{job.Id}")).Content.ReadAsStringAsync());

        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("expiresAt").ValueKind);
        Assert.False(body.RootElement.GetProperty("isExpired").GetBoolean());
    }

    // --- POST /api/jobs ---

    [Fact]
    public async Task CreateJob_WithoutStatus_DefaultsToActive()
    {
        using var admin = await _api.AdminClientAsync();
        var payload = new { title = "T", company = "C", description = "D", location = "L", workType = 0 };

        var response = await admin.PostJsonAsync("/api/jobs", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(JobOfferStatus.Active, job.Status);
        Assert.Null(job.ExpiresAt);
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Active)]
    [InlineData(JobOfferStatus.Closed)]
    public async Task CreateJob_WithStatus_StoresIt(JobOfferStatus status)
    {
        var job = await _api.CreateJobWithStatusAsync(status);

        Assert.Equal(status, job.Status);
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(status, (await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task CreateJob_ActiveWithPastExpiresAt_ReturnsBadRequestWithExpiresAtError()
    {
        using var admin = await _api.AdminClientAsync();
        var dto = ApiFixture.NewJobDto(j => { j.Status = JobOfferStatus.Active; j.ExpiresAt = DateTime.UtcNow.AddMinutes(-5); });

        var response = await admin.PostJsonAsync("/api/jobs", dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var errors = await ErrorsAsync(response);
        Assert.Equal("ExpiresAt must be in the future for an active job offer", errors.GetProperty("ExpiresAt")[0].GetString());
    }

    [Fact]
    public async Task CreateJob_WithoutStatusButPastExpiresAt_ReturnsBadRequest()
    {
        // brak statusu = Active, więc data w przeszłości jest odrzucana
        using var admin = await _api.AdminClientAsync();
        var past = DateTime.UtcNow.AddDays(-1).ToString("O");
        var json = $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"{{past}}"}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("ExpiresAt", out _));
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Closed)]
    public async Task CreateJob_DraftOrClosedWithPastExpiresAt_Succeeds(JobOfferStatus status)
    {
        var past = DateTime.UtcNow.AddDays(-7);

        var job = await _api.CreateJobWithStatusAsync(status, past);

        Assert.Equal(status, job.Status);
        Assert.True(Math.Abs((job.ExpiresAt!.Value - past).TotalSeconds) < 1);
        // oferta nieaktywna nie jest "wygasła" nawet z datą w przeszłości
        Assert.False(job.IsExpired);
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("3")]
    public async Task CreateJob_WithUndefinedStatusNumber_ReturnsBadRequest(string status)
    {
        using var admin = await _api.AdminClientAsync();
        var json = $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"status":{{status}}}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("Status", out _));
    }

    [Theory]
    [InlineData("\"published\"")]
    [InlineData("\"foo\"")]
    [InlineData("true")]
    [InlineData("{}")]
    public async Task CreateJob_WithNonEnumStatus_ReturnsBadRequest(string status)
    {
        using var admin = await _api.AdminClientAsync();
        var json = $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"status":{{status}}}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateJob_WithInvalidExpiresAtFormat_ReturnsBadRequest()
    {
        using var admin = await _api.AdminClientAsync();
        var json = """{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"not-a-date"}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateJob_ExpiresAtWithoutTimeZone_IsTreatedAsUtc()
    {
        using var admin = await _api.AdminClientAsync();
        var wallClock = new DateTime(2099, 6, 15, 10, 30, 0, DateTimeKind.Unspecified);
        var json = $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"{{wallClock:yyyy-MM-ddTHH:mm:ss}}"}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2099-06-15T10:30:00Z", body.RootElement.GetProperty("expiresAt").GetString());

        // ta sama wartość po odczycie z bazy
        var id = body.RootElement.GetProperty("id").GetInt32();
        using var anonymous = _api.CreateClient();
        using var fetched = JsonDocument.Parse(await (await anonymous.GetAsync($"/api/jobs/{id}")).Content.ReadAsStringAsync());
        Assert.Equal("2099-06-15T10:30:00Z", fetched.RootElement.GetProperty("expiresAt").GetString());
    }

    [Fact]
    public async Task CreateJob_ExpiresAtWithOffset_IsConvertedToUtc()
    {
        using var admin = await _api.AdminClientAsync();
        var json = """{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"2099-06-15T10:30:00+02:00"}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("2099-06-15T08:30:00Z", body.RootElement.GetProperty("expiresAt").GetString());
    }

    [Fact]
    public async Task CreateJob_ExpiresAtInThePastOnlyAfterApplyingOffset_ReturnsBadRequest()
    {
        // ta sama godzina ścienna: bez strefy (UTC) leży w przyszłości, z offsetem +14:00 - w przeszłości
        var wallClock = DateTime.UtcNow.AddHours(13).AddMinutes(30);
        var text = wallClock.ToString("yyyy-MM-ddTHH:mm:ss");
        using var admin = await _api.AdminClientAsync();

        var withoutZone = await admin.PostRawJsonAsync("/api/jobs",
            $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"{{text}}"}""");
        var withOffset = await admin.PostRawJsonAsync("/api/jobs",
            $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"{{text}}+14:00"}""");

        Assert.Equal(HttpStatusCode.Created, withoutZone.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withOffset.StatusCode);
    }

    [Fact]
    public async Task CreateJob_WithNullExpiresAt_CreatesOpenEndedOffer()
    {
        using var admin = await _api.AdminClientAsync();
        var json = """{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":null}""";

        var response = await admin.PostRawJsonAsync("/api/jobs", json);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Null((await response.ReadJsonAsync<JobOfferDto>()).ExpiresAt);
    }

    // --- PUT /api/jobs/{id} ---

    [Fact]
    public async Task UpdateJob_ChangesStatusAndExpiresAt()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft);
        var newExpiry = DateTime.UtcNow.AddDays(20);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"/api/jobs/{job.Id}",
            ApiFixture.NewJobDto(j => { j.Status = JobOfferStatus.Active; j.ExpiresAt = newExpiry; }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(JobOfferStatus.Active, updated.Status);
        Assert.True(Math.Abs((updated.ExpiresAt!.Value - newExpiry).TotalSeconds) < 1);
        var fetched = await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(JobOfferStatus.Active, fetched.Status);
    }

    [Fact]
    public async Task UpdateJob_CanClearExpiresAt()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(5));
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"/api/jobs/{job.Id}", ApiFixture.NewJobDto(j => j.ExpiresAt = null));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null((await response.ReadJsonAsync<JobOfferDto>()).ExpiresAt);
    }

    [Fact]
    public async Task UpdateJob_ActiveWithPastExpiresAt_ReturnsBadRequestAndKeepsOffer()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(5));
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"/api/jobs/{job.Id}",
            ApiFixture.NewJobDto(j => { j.Status = JobOfferStatus.Active; j.ExpiresAt = DateTime.UtcNow.AddHours(-1); j.Title = "Changed"; }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("ExpiresAt", out _));
        var fetched = await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(job.Title, fetched.Title);
        Assert.True(fetched.ExpiresAt > DateTime.UtcNow);
    }

    [Fact]
    public async Task UpdateJob_DraftWithPastExpiresAt_Succeeds()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"/api/jobs/{job.Id}",
            ApiFixture.NewJobDto(j => { j.Status = JobOfferStatus.Draft; j.ExpiresAt = DateTime.UtcNow.AddDays(-1); }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(JobOfferStatus.Draft, updated.Status);
        Assert.False(updated.IsExpired);
    }

    [Fact]
    public async Task UpdateJob_WithUndefinedStatus_ReturnsBadRequest()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();
        var json = """{"title":"T","company":"C","description":"D","location":"L","workType":0,"status":42}""";

        var response = await admin.PutRawJsonAsync($"/api/jobs/{job.Id}", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("Status", out _));
    }

    [Fact]
    public async Task UpdateJob_UnpublishingRemovesOfferFromPublicList()
    {
        var token = NewToken();
        var job = await _api.CreateJobAsync(j => j.Description = $"lifecycle {token}");
        Assert.Single(await PublicJobsAsync(token));
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"/api/jobs/{job.Id}",
            ApiFixture.NewJobDto(j => { j.Description = $"lifecycle {token}"; j.Status = JobOfferStatus.Draft; }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await PublicJobsAsync(token));
    }

    // PUT bez pola status zachowuje obecny status - edycja treści nie publikuje szkicu ani nie wznawia zamkniętej oferty
    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Active)]
    [InlineData(JobOfferStatus.Closed)]
    public async Task UpdateJob_WithoutStatusField_KeepsCurrentStatus(JobOfferStatus status)
    {
        var job = await _api.CreateJobWithStatusAsync(status);
        using var admin = await _api.AdminClientAsync();
        var json = """{"title":"Edited","company":"C","description":"D","location":"L","workType":0}""";

        var response = await admin.PutRawJsonAsync($"/api/jobs/{job.Id}", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(status, updated.Status);
        Assert.Equal("Edited", updated.Title);
        var fetched = await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(status, fetched.Status);
    }

    [Fact]
    public async Task UpdateJob_WithoutStatusFieldOnClosedOfferWithPastExpiresAt_Succeeds()
    {
        var closed = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, DateTime.UtcNow.AddDays(-3));
        using var admin = await _api.AdminClientAsync();
        var json = $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"{{DateTime.UtcNow.AddDays(-1):O}}"}""";

        var response = await admin.PutRawJsonAsync($"/api/jobs/{closed.Id}", json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JobOfferStatus.Closed, (await response.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task UpdateJob_WithoutStatusFieldOnActiveOfferWithPastExpiresAt_ReturnsBadRequest()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(5));
        using var admin = await _api.AdminClientAsync();
        var json = $$"""{"title":"T","company":"C","description":"D","location":"L","workType":0,"expiresAt":"{{DateTime.UtcNow.AddDays(-1):O}}"}""";

        var response = await admin.PutRawJsonAsync($"/api/jobs/{job.Id}", json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("ExpiresAt must be in the future for an active job offer", (await ErrorsAsync(response)).GetProperty("ExpiresAt")[0].GetString());
    }

    // --- PATCH /api/jobs/{id}/status ---

    [Fact]
    public async Task PatchStatus_WithoutToken_ReturnsUnauthorized()
    {
        var job = await _api.CreateJobAsync();
        using var client = _api.CreateClient();

        var response = await client.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_AsRegularUser_ReturnsForbiddenAndKeepsStatus()
    {
        var job = await _api.CreateJobAsync();
        using var user = await _api.UserClientAsync();

        var response = await user.PatchJsonAsync($"/api/jobs/{job.Id}/status", new UpdateJobOfferStatusDto { Status = JobOfferStatus.Closed });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(JobOfferStatus.Active, (await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task PatchStatus_PublishDraft_MakesOfferPublic()
    {
        var token = NewToken();
        var draft = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, null, j => j.Description = $"lifecycle {token}");
        Assert.Empty(await PublicJobsAsync(token));

        var response = await PatchStatusAsync(draft.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(draft.Id, updated.Id);
        Assert.Equal(JobOfferStatus.Active, updated.Status);
        Assert.Equal(draft.Title, updated.Title);
        Assert.Single(await PublicJobsAsync(token));
    }

    [Fact]
    public async Task PatchStatus_CloseAndReopen_TogglesPublicVisibility()
    {
        var token = NewToken();
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Active, DateTime.UtcNow.AddDays(10), j => j.Description = $"lifecycle {token}");

        var closeResponse = await PatchStatusAsync(job.Id, JobOfferStatus.Closed);
        Assert.Equal(HttpStatusCode.OK, closeResponse.StatusCode);
        Assert.Equal(JobOfferStatus.Closed, (await closeResponse.ReadJsonAsync<JobOfferDto>()).Status);
        Assert.Empty(await PublicJobsAsync(token));

        var reopenResponse = await PatchStatusAsync(job.Id, JobOfferStatus.Active);
        Assert.Equal(HttpStatusCode.OK, reopenResponse.StatusCode);
        Assert.Single(await PublicJobsAsync(token));
    }

    [Fact]
    public async Task PatchStatus_ActiveBackToDraft_Succeeds()
    {
        var job = await _api.CreateJobAsync();

        var response = await PatchStatusAsync(job.Id, JobOfferStatus.Draft);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JobOfferStatus.Draft, (await response.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task PatchStatus_ToCurrentStatus_IsIdempotent()
    {
        var job = await _api.CreateJobAsync();

        var response = await PatchStatusAsync(job.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JobOfferStatus.Active, (await response.ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Theory]
    [InlineData(JobOfferStatus.Draft)]
    [InlineData(JobOfferStatus.Closed)]
    public async Task PatchStatus_PublishOfferWithPastExpiresAt_ReturnsBadRequestWithMessageAndKeepsStatus(JobOfferStatus initial)
    {
        var job = await _api.CreateJobWithStatusAsync(initial, DateTime.UtcNow.AddDays(-2));

        var response = await PatchStatusAsync(job.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Expiration date must be in the future to publish", await MessageAsync(response));
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(initial, (await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task PatchStatus_ActiveToActiveForExpiredOffer_ReturnsBadRequest()
    {
        var expired = await _api.CreateExpiredJobAsync();

        var response = await PatchStatusAsync(expired.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(JobOfferStatus.Closed)]
    [InlineData(JobOfferStatus.Draft)]
    public async Task PatchStatus_ClosingOrUnpublishingExpiredOffer_Succeeds(JobOfferStatus target)
    {
        var expired = await _api.CreateExpiredJobAsync();

        var response = await PatchStatusAsync(expired.Id, target);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(target, updated.Status);
        Assert.False(updated.IsExpired);
    }

    [Fact]
    public async Task PatchStatus_PublishOfferAfterExtendingExpiresAt_Succeeds()
    {
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, DateTime.UtcNow.AddDays(-2));
        using var admin = await _api.AdminClientAsync();
        var extend = await admin.PutJsonAsync($"/api/jobs/{job.Id}",
            ApiFixture.NewJobDto(j => { j.Status = JobOfferStatus.Closed; j.ExpiresAt = DateTime.UtcNow.AddDays(14); }));
        Assert.Equal(HttpStatusCode.OK, extend.StatusCode);

        var response = await PatchStatusAsync(job.Id, JobOfferStatus.Active);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task PatchStatus_NonexistentOffer_ReturnsNotFound()
    {
        var response = await PatchStatusAsync(int.MaxValue, JobOfferStatus.Closed);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Job offer not found", await MessageAsync(response));
    }

    [Theory]
    [InlineData("99")]
    [InlineData("-1")]
    [InlineData("3")]
    [InlineData("\"closed\"")]
    [InlineData("null")]
    public async Task PatchStatus_WithInvalidStatus_ReturnsBadRequest(string status)
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PatchRawJsonAsync($"/api/jobs/{job.Id}/status", $$"""{"status":{{status}}}""");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(JobOfferStatus.Active, (await (await admin.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>()).Status);
    }

    [Fact]
    public async Task PatchStatus_WithoutStatusField_ReturnsBadRequest()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PatchRawJsonAsync($"/api/jobs/{job.Id}/status", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.True((await ErrorsAsync(response)).TryGetProperty("Status", out _));
    }
}
