using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Tworzenie, edycja i usuwanie ofert - tylko dla admina
[Collection(ApiCollection.Name)]
public class JobsCrudTests
{
    private readonly ApiFixture _api;

    public JobsCrudTests(ApiFixture api) => _api = api;

    // --- tworzenie ---

    [Fact]
    public async Task CreateJob_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/jobs", ApiFixture.NewJobDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateJob_AsRegularUser_ReturnsForbidden()
    {
        using var client = await _api.UserClientAsync();

        var response = await client.PostJsonAsync("/api/jobs", ApiFixture.NewJobDto());

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateJob_AsAdmin_ReturnsCreatedWithLocationAndBody()
    {
        using var client = await _api.AdminClientAsync();
        var dto = ApiFixture.NewJobDto(j =>
        {
            j.Title = "Created by admin";
            j.WorkType = WorkType.Hybrid;
            j.SalaryMin = 7000;
            j.SalaryMax = 9000;
        });

        var response = await client.PostJsonAsync("/api/jobs", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.True(job.Id > 0);
        Assert.Equal("Created by admin", job.Title);
        Assert.Equal(WorkType.Hybrid, job.WorkType);
        Assert.Equal(7000, job.SalaryMin);
        Assert.True(Math.Abs((DateTime.UtcNow - job.CreatedAt).TotalMinutes) < 5);
        Assert.EndsWith($"/api/Jobs/{job.Id}", response.Headers.Location?.ToString());

        var fetched = await client.GetAsync($"/api/jobs/{job.Id}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
    }

    [Fact]
    public async Task CreateJob_WithoutSalary_Succeeds()
    {
        using var client = await _api.AdminClientAsync();
        var dto = ApiFixture.NewJobDto(j => { j.SalaryMin = null; j.SalaryMax = null; });

        var response = await client.PostJsonAsync("/api/jobs", dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Null(job.SalaryMin);
        Assert.Null(job.SalaryMax);
    }

    [Fact]
    public async Task CreateJob_IgnoresClientProvidedCreatedAt()
    {
        using var client = await _api.AdminClientAsync();
        var payload = new { title = "T", company = "C", description = "D", location = "L", workType = 0, createdAt = "2001-01-01T00:00:00Z" };

        var response = await client.PostJsonAsync("/api/jobs", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.True(job.CreatedAt.Year >= 2025);
    }

    [Theory]
    [InlineData("title", "")]
    [InlineData("title", "   ")]
    [InlineData("company", "")]
    [InlineData("description", "")]
    [InlineData("location", "")]
    public async Task CreateJob_WithMissingRequiredField_ReturnsBadRequest(string field, string value)
    {
        using var client = await _api.AdminClientAsync();
        var payload = new Dictionary<string, object?>
        {
            ["title"] = "Title", ["company"] = "Company", ["description"] = "Description", ["location"] = "Location", ["workType"] = 0
        };
        payload[field] = value;

        var response = await client.PostJsonAsync("/api/jobs", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CreateJob_WithTitleLongerThan200Characters_ReturnsBadRequest()
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.PostJsonAsync("/api/jobs", ApiFixture.NewJobDto(j => j.Title = new string('x', 201)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(99)]
    [InlineData(-1)]
    [InlineData(3)]
    public async Task CreateJob_WithUndefinedWorkType_ReturnsBadRequest(int workType)
    {
        using var client = await _api.AdminClientAsync();
        var payload = new { title = "T", company = "C", description = "D", location = "L", workType };

        var response = await client.PostJsonAsync("/api/jobs", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("WorkType", out _));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task CreateJob_WithEachDefinedWorkType_Succeeds(int workType)
    {
        using var client = await _api.AdminClientAsync();
        var payload = new { title = "T", company = "C", description = "D", location = "L", workType };

        var response = await client.PostJsonAsync("/api/jobs", payload);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal((WorkType)workType, (await response.ReadJsonAsync<JobOfferDto>()).WorkType);
    }

    [Fact]
    public async Task UpdateJob_WithUndefinedWorkType_ReturnsBadRequestAndKeepsOffer()
    {
        var job = await _api.CreateJobAsync(j => j.WorkType = WorkType.Onsite);
        using var client = await _api.AdminClientAsync();
        var payload = new { title = "T", company = "C", description = "D", location = "L", workType = 99 };

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", payload);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fetched = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(WorkType.Onsite, fetched.WorkType);
    }

    private static decimal? Salary(string? value) =>
        value == null ? null : decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    [Fact]
    public async Task CreateJob_WithSalaryMinGreaterThanSalaryMax_ReturnsBadRequest()
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.PostJsonAsync("/api/jobs", ApiFixture.NewJobDto(j => { j.SalaryMin = 20000; j.SalaryMax = 1000; }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.Equal("SalaryMin cannot be greater than SalaryMax", errors.GetProperty("SalaryMin")[0].GetString());
    }

    [Theory]
    [InlineData("-1", null)]
    [InlineData(null, "-0.01")]
    [InlineData("-5000", "-1000")]
    [InlineData("10000000000000000", null)]
    public async Task CreateJob_WithNegativeOrTooLargeSalary_ReturnsBadRequest(string? min, string? max)
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.PostJsonAsync("/api/jobs",
            ApiFixture.NewJobDto(j => { j.SalaryMin = Salary(min); j.SalaryMax = Salary(max); }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("0", "0")]
    [InlineData("5000", "5000")]
    [InlineData(null, "8000")]
    [InlineData("8000", null)]
    [InlineData("1500.50", "9999999999999999.99")]
    public async Task CreateJob_WithValidSalaryRange_Succeeds(string? min, string? max)
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.PostJsonAsync("/api/jobs",
            ApiFixture.NewJobDto(j => { j.SalaryMin = Salary(min); j.SalaryMax = Salary(max); }));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var job = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(Salary(min), job.SalaryMin);
        Assert.Equal(Salary(max), job.SalaryMax);
    }

    [Fact]
    public async Task UpdateJob_WithSalaryMinGreaterThanSalaryMax_ReturnsBadRequestAndKeepsOffer()
    {
        var job = await _api.CreateJobAsync(j => { j.SalaryMin = 5000; j.SalaryMax = 8000; });
        using var client = await _api.AdminClientAsync();

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", ApiFixture.NewJobDto(j => { j.SalaryMin = 9000; j.SalaryMax = 8000; }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fetched = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(5000, fetched.SalaryMin);
    }

    // --- edycja ---

    [Fact]
    public async Task UpdateJob_WithoutToken_ReturnsUnauthorized()
    {
        var job = await _api.CreateJobAsync();
        using var client = _api.CreateClient();

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", ApiFixture.NewJobDto());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task UpdateJob_AsRegularUser_ReturnsForbiddenAndDoesNotChangeOffer()
    {
        var job = await _api.CreateJobAsync(j => j.Title = "Original title");
        using var client = await _api.UserClientAsync();

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", ApiFixture.NewJobDto(j => j.Title = "Hijacked"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var fetched = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal("Original title", fetched.Title);
    }

    [Fact]
    public async Task UpdateJob_AsAdmin_ReplacesEditableFieldsAndKeepsIdAndCreatedAt()
    {
        var job = await _api.CreateJobAsync(j => { j.Title = "Before"; j.WorkType = WorkType.Remote; });
        using var client = await _api.AdminClientAsync();
        // CreatedAt z odpowiedzi POST ma wyższą precyzję niż zapisana w bazie - porównujemy z odczytem z bazy
        var before = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        var update = ApiFixture.NewJobDto(j =>
        {
            j.Title = "After";
            j.Company = "New company";
            j.Description = "New description";
            j.Location = "New city";
            j.SalaryMin = 11111;
            j.SalaryMax = 22222;
            j.WorkType = WorkType.Onsite;
        });

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var updated = await response.ReadJsonAsync<JobOfferDto>();
        Assert.Equal(job.Id, updated.Id);
        Assert.Equal("After", updated.Title);
        Assert.Equal(WorkType.Onsite, updated.WorkType);

        var fetched = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal("After", fetched.Title);
        Assert.Equal("New company", fetched.Company);
        Assert.Equal("New description", fetched.Description);
        Assert.Equal("New city", fetched.Location);
        Assert.Equal(11111, fetched.SalaryMin);
        Assert.Equal(22222, fetched.SalaryMax);
        Assert.Equal(WorkType.Onsite, fetched.WorkType);
        Assert.Equal(before.CreatedAt, fetched.CreatedAt);
    }

    [Fact]
    public async Task UpdateJob_CanClearSalary()
    {
        var job = await _api.CreateJobAsync(j => { j.SalaryMin = 1000; j.SalaryMax = 2000; });
        using var client = await _api.AdminClientAsync();

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", ApiFixture.NewJobDto(j => { j.SalaryMin = null; j.SalaryMax = null; }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var fetched = await (await client.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Null(fetched.SalaryMin);
        Assert.Null(fetched.SalaryMax);
    }

    [Fact]
    public async Task UpdateJob_WithUnknownId_ReturnsNotFound()
    {
        using var client = await _api.AdminClientAsync();

        var response = await client.PutJsonAsync("/api/jobs/999999999", ApiFixture.NewJobDto());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task UpdateJob_WithInvalidBody_ReturnsBadRequest()
    {
        var job = await _api.CreateJobAsync();
        using var client = await _api.AdminClientAsync();

        var response = await client.PutJsonAsync($"/api/jobs/{job.Id}", ApiFixture.NewJobDto(j => j.Title = string.Empty));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- usuwanie ---

    [Fact]
    public async Task DeleteJob_WithoutToken_ReturnsUnauthorized()
    {
        var job = await _api.CreateJobAsync();
        using var client = _api.CreateClient();

        var response = await client.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DeleteJob_AsRegularUser_ReturnsForbiddenAndKeepsOffer()
    {
        var job = await _api.CreateJobAsync();
        using var user = await _api.UserClientAsync();

        var response = await user.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await user.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteJob_AsAdmin_ReturnsNoContentAndOfferDisappears()
    {
        var job = await _api.CreateJobAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task DeleteJob_WithUnknownId_ReturnsNotFound()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync("/api/jobs/999999999");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("Job offer not found", body.RootElement.GetProperty("message").GetString());
    }

    [Fact]
    public async Task DeleteJob_RemovesApplicationsToThatOffer()
    {
        var job = await _api.CreateJobAsync();
        var applicant = await _api.RegisterUserAsync("cascade");
        await _api.ApplyAsync(applicant, job.Id);
        using var admin = await _api.AdminClientAsync();
        using var userClient = _api.CreateClient(applicant);

        var response = await admin.DeleteAsync($"/api/jobs/{job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var mine = await (await userClient.GetAsync("/api/jobapplications/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Empty(mine);
    }
}
