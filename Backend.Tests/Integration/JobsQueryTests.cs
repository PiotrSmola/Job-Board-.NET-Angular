using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Lista ofert (paginacja, sortowanie, filtry) i pobieranie po id. Dane seeda są współdzielone,
// więc testy sortowania/filtrów tworzą własne oferty oznaczone unikalnym tokenem w opisie
// i zawężają wyniki przez searchTerm.
[Collection(ApiCollection.Name)]
public class JobsQueryTests
{
    private readonly ApiFixture _api;

    public JobsQueryTests(ApiFixture api) => _api = api;

    // Cztery oferty z różnym tytułem, firmą, lokalizacją i widełkami; zwracane w kolejności tworzenia (J1..J4)
    private async Task<(string Token, JobOfferDto[] Jobs)> CreateSortableJobsAsync()
    {
        var token = Guid.NewGuid().ToString("N");
        var jobs = new[]
        {
            await _api.CreateJobAsync(j => { j.Title = "B-title"; j.Company = "Zeta"; j.Location = "Lublin"; j.SalaryMin = 3000; j.SalaryMax = 9000; j.WorkType = WorkType.Remote; j.Description = $"desc {token}"; }),
            await _api.CreateJobAsync(j => { j.Title = "C-title"; j.Company = "Alpha"; j.Location = "Kielce"; j.SalaryMin = 1000; j.SalaryMax = 12000; j.WorkType = WorkType.Hybrid; j.Description = $"desc {token}"; }),
            await _api.CreateJobAsync(j => { j.Title = "A-title"; j.Company = "Mid"; j.Location = "Opole"; j.SalaryMin = 2000; j.SalaryMax = 7000; j.WorkType = WorkType.Hybrid; j.Description = $"desc {token}"; }),
            await _api.CreateJobAsync(j => { j.Title = "D-title"; j.Company = "Omega"; j.Location = "Gdynia"; j.SalaryMin = null; j.SalaryMax = null; j.WorkType = WorkType.Onsite; j.Description = $"desc {token}"; })
        };
        return (token, jobs);
    }

    private async Task<(HttpResponseMessage Response, JobOfferDto[] Items)> GetJobsAsync(string query)
    {
        using var client = _api.CreateClient();
        var response = await client.GetAsync("/api/jobs" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, await response.ReadJsonAsync<JobOfferDto[]>());
    }

    // --- paginacja ---

    [Fact]
    public async Task GetJobs_WithoutParameters_ReturnsFirstPageOfTenWithPaginationHeader()
    {
        var (response, items) = await GetJobsAsync(string.Empty);

        var pagination = response.ReadPagination();
        Assert.Equal(1, pagination.CurrentPage);
        Assert.Equal(10, pagination.ItemsPerPage);
        Assert.True(pagination.TotalItems >= 20, "Seed contains 20 job offers");
        Assert.Equal((int)Math.Ceiling(pagination.TotalItems / 10.0), pagination.TotalPages);
        Assert.Equal(10, items.Length);
    }

    [Fact]
    public async Task GetJobs_IsPubliclyAccessible()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetJobs_WithPageSizeAboveLimit_IsClampedToFifty()
    {
        var (response, items) = await GetJobsAsync("?pageSize=500");

        Assert.Equal(50, response.ReadPagination().ItemsPerPage);
        Assert.True(items.Length <= 50);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public async Task GetJobs_WithPageNumberBelowOne_IsNormalizedToFirstPage(int pageNumber)
    {
        var (response, items) = await GetJobsAsync($"?pageNumber={pageNumber}");

        Assert.Equal(1, response.ReadPagination().CurrentPage);
        Assert.NotEmpty(items);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task GetJobs_WithNonPositivePageSize_FallsBackToDefaultTen(int pageSize)
    {
        var (response, items) = await GetJobsAsync($"?pageSize={pageSize}");

        Assert.Equal(10, response.ReadPagination().ItemsPerPage);
        Assert.Equal(10, items.Length);
    }

    [Fact]
    public async Task GetJobs_PageBeyondLastPage_ReturnsEmptyListWithHeader()
    {
        var (response, items) = await GetJobsAsync("?pageNumber=9999&pageSize=10");

        var pagination = response.ReadPagination();
        Assert.Equal(9999, pagination.CurrentPage);
        Assert.Empty(items);
    }

    [Fact]
    public async Task GetJobs_ConsecutivePages_DoNotOverlapAndCoverAllResults()
    {
        var token = Guid.NewGuid().ToString("N");
        var created = new List<int>();
        for (var i = 0; i < 12; i++)
        {
            var index = i;
            created.Add((await _api.CreateJobAsync(j => { j.Title = $"Paged {index:D2}"; j.Description = $"desc {token}"; })).Id);
        }

        var seen = new List<int>();
        PaginationPageInfo? last = null;
        for (var page = 1; page <= 3; page++)
        {
            var (response, items) = await GetJobsAsync($"?searchTerm={token}&pageSize=5&pageNumber={page}&orderBy=titleAsc");
            var pagination = response.ReadPagination();
            last = new PaginationPageInfo(pagination.TotalItems, pagination.TotalPages, pagination.ItemsPerPage);
            Assert.Equal(page, pagination.CurrentPage);
            Assert.Equal(page < 3 ? 5 : 2, items.Length);
            seen.AddRange(items.Select(i => i.Id));
        }

        Assert.Equal(new PaginationPageInfo(12, 3, 5), last);
        Assert.Equal(12, seen.Distinct().Count());
        Assert.Equal(created.OrderBy(id => id), seen.OrderBy(id => id));
    }

    private sealed record PaginationPageInfo(int TotalItems, int TotalPages, int ItemsPerPage);

    // --- sortowanie ---

    [Theory]
    [InlineData("titleAsc", "3,1,2,4")]
    [InlineData("titleDesc", "4,2,1,3")]
    [InlineData("companyAsc", "2,3,4,1")]
    [InlineData("companyDesc", "1,4,3,2")]
    [InlineData("locationAsc", "4,2,1,3")]    // Gdynia, Kielce, Lublin, Opole
    [InlineData("locationDesc", "3,1,2,4")]
    [InlineData("newest", "4,3,2,1")]
    [InlineData("oldest", "1,2,3,4")]
    [InlineData("salaryHigh", "2,1,3,4")]     // SalaryMax malejąco, brak widełek na końcu
    [InlineData("salaryLow", "2,3,1,4")]      // SalaryMin rosnąco, brak widełek na końcu
    [InlineData("unknownOrder", "4,3,2,1")]   // nieznany klucz = domyślnie najnowsze
    public async Task GetJobs_OrderBy_ReturnsExpectedSequence(string orderBy, string expectedSequence)
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&orderBy={orderBy}");

        var expectedIds = expectedSequence.Split(',').Select(n => jobs[int.Parse(n) - 1].Id).ToArray();
        Assert.Equal(expectedIds, items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task GetJobs_WithoutOrderBy_DefaultsToNewestFirst()
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}");

        Assert.Equal(jobs.Select(j => j.Id).Reverse().ToArray(), items.Select(i => i.Id).ToArray());
    }

    // --- filtry ---

    [Fact]
    public async Task GetJobs_SearchTerm_IsCaseInsensitiveAndMatchesTitleCompanyAndDescription()
    {
        var token = Guid.NewGuid().ToString("N");
        var byTitle = await _api.CreateJobAsync(j => j.Title = $"Title-{token}");
        var byCompany = await _api.CreateJobAsync(j => j.Company = $"Company-{token}");
        var byDescription = await _api.CreateJobAsync(j => j.Description = $"Description {token}");
        await _api.CreateJobAsync(j => j.Title = "Unrelated offer");

        var (response, items) = await GetJobsAsync($"?searchTerm={token.ToUpperInvariant()}");

        Assert.Equal(
            new[] { byTitle.Id, byCompany.Id, byDescription.Id }.OrderBy(id => id),
            items.Select(i => i.Id).OrderBy(id => id));
        Assert.Equal(3, response.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task GetJobs_SearchTermWithoutMatches_ReturnsEmptyListAndZeroTotals()
    {
        var (response, items) = await GetJobsAsync($"?searchTerm=nomatch{Guid.NewGuid():N}");

        Assert.Empty(items);
        var pagination = response.ReadPagination();
        Assert.Equal(0, pagination.TotalItems);
        Assert.Equal(0, pagination.TotalPages);
    }

    [Fact]
    public async Task GetJobs_WhitespaceSearchTerm_IsIgnored()
    {
        var (response, _) = await GetJobsAsync("?searchTerm=%20%20");

        Assert.True(response.ReadPagination().TotalItems >= 20);
    }

    [Theory]
    [InlineData(0, "1")]
    [InlineData(1, "2,3")]
    [InlineData(2, "4")]
    public async Task GetJobs_WorkTypeFilter_ReturnsOnlyMatchingOffers(int workType, string expectedJobs)
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&workType={workType}&orderBy=oldest");

        var expectedIds = expectedJobs.Split(',').Select(n => jobs[int.Parse(n) - 1].Id).ToArray();
        Assert.Equal(expectedIds, items.Select(i => i.Id).ToArray());
        Assert.All(items, i => Assert.Equal((WorkType)workType, i.WorkType));
    }

    [Fact]
    public async Task GetJobs_WorkTypeFilterByName_IsAccepted()
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&workType=Onsite");

        Assert.Equal(new[] { jobs[3].Id }, items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task GetJobs_LocationFilter_IsPartialAndCaseInsensitive()
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&location=KIEL");

        Assert.Equal(new[] { jobs[1].Id }, items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task GetJobs_MinSalaryOnly_MatchesOffersReachingThatSalary()
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&minSalary=8000&orderBy=oldest");

        Assert.Equal(new[] { jobs[0].Id, jobs[1].Id }, items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task GetJobs_MaxSalaryOnly_MatchesOffersStartingBelowThatSalary()
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&maxSalary=2500&orderBy=oldest");

        Assert.Equal(new[] { jobs[1].Id, jobs[2].Id }, items.Select(i => i.Id).ToArray());
    }

    [Fact]
    public async Task GetJobs_MinAndMaxSalary_RequiresRangeInsideBounds()
    {
        var (token, jobs) = await CreateSortableJobsAsync();

        var (_, items) = await GetJobsAsync($"?searchTerm={token}&minSalary=1500&maxSalary=9500&orderBy=oldest");

        Assert.Equal(new[] { jobs[0].Id, jobs[2].Id }, items.Select(i => i.Id).ToArray());
    }

    // --- szczegóły oferty ---

    [Fact]
    public async Task GetJob_WithExistingId_ReturnsFullOffer()
    {
        var created = await _api.CreateJobAsync(j =>
        {
            j.Title = "Detail title";
            j.Company = "Detail company";
            j.Location = "Detail city";
            j.SalaryMin = 4000;
            j.SalaryMax = 6000;
            j.WorkType = WorkType.Onsite;
        });
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{created.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(created.Id, job.Id);
        Assert.Equal("Detail title", job.Title);
        Assert.Equal("Detail company", job.Company);
        Assert.Equal("Detail city", job.Location);
        Assert.Equal(4000, job.SalaryMin);
        Assert.Equal(6000, job.SalaryMax);
        Assert.Equal(WorkType.Onsite, job.WorkType);
        Assert.True(Math.Abs((DateTime.UtcNow - job.CreatedAt).TotalMinutes) < 5);
    }

    [Fact]
    public async Task GetJob_WithUnknownId_ReturnsNotFoundWithMessage()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobs/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Job offer not found", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task GetJob_WithNonNumericId_ReturnsBadRequest()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobs/abc");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetJobs_SerializesWorkTypeAsNumberAndPropertiesAsCamelCase()
    {
        var created = await _api.CreateJobAsync(j => j.WorkType = WorkType.Onsite);
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs/{created.Id}");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;
        Assert.Equal(JsonValueKind.Number, root.GetProperty("workType").ValueKind);
        Assert.Equal(2, root.GetProperty("workType").GetInt32());
        Assert.True(root.TryGetProperty("createdAt", out _));
        Assert.True(root.TryGetProperty("salaryMin", out _));
    }
}
