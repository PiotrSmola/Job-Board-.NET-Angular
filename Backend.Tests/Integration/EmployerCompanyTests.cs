using System.Net;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// /api/employer/company: podgląd i edycja profilu własnej firmy (ponowne zgłoszenie odrzuconej firmy)
[Collection(ApiCollection.Name)]
public class EmployerCompanyTests
{
    private const string Url = "/api/employer/company";
    private const string NameTakenMessage = "Company name is already taken";

    private readonly ApiFixture _api;

    public EmployerCompanyTests(ApiFixture api) => _api = api;

    private static CompanyFormDto Form(string name, Action<CompanyFormDto>? customize = null)
    {
        var dto = new CompanyFormDto { Name = name };
        customize?.Invoke(dto);
        return dto;
    }

    private async Task<CompanyDto> GetOwnAsync(TestEmployer employer)
    {
        using var client = _api.CreateClient(employer);
        var response = await client.GetAsync(Url);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CompanyDto>();
    }

    // --- autoryzacja ---

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Endpoint_WithoutToken_ReturnsUnauthorized(string method)
    {
        using var client = _api.CreateClient();

        var response = method == "GET"
            ? await client.GetAsync(Url)
            : await client.PutJsonAsync(Url, Form("X"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Endpoint_AsRegularUser_ReturnsForbidden(string method)
    {
        using var client = await _api.UserClientAsync();

        var response = method == "GET"
            ? await client.GetAsync(Url)
            : await client.PutJsonAsync(Url, Form("X"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("PUT")]
    public async Task Endpoint_AsAdmin_ReturnsForbidden(string method)
    {
        using var client = await _api.AdminClientAsync();

        var response = method == "GET"
            ? await client.GetAsync(Url)
            : await client.PutJsonAsync(Url, Form("X"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // --- podgląd ---

    [Fact]
    public async Task Get_ReturnsOwnCompanyWithStatusAndRejectionReason()
    {
        var employer = await _api.RegisterEmployerAsync(customize: d =>
        {
            d.CompanyDescription = "Opis";
            d.Location = "Rzeszów";
        });
        await _api.RejectCompanyAsync(employer.CompanyId, "Uzupełnij dane");

        var company = await GetOwnAsync(employer);

        Assert.Equal(employer.CompanyId, company.Id);
        Assert.Equal(employer.CompanyName, company.Name);
        Assert.Equal("Opis", company.Description);
        Assert.Equal(CompanyStatus.Rejected, company.Status);
        Assert.Equal("Uzupełnij dane", company.RejectionReason);
        Assert.NotNull(company.ReviewedAt);
    }

    [Fact]
    public async Task Get_ReturnsActiveJobsCountOfOwnCompany()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        await _api.CreateEmployerJobAsync(employer);
        await _api.CreateEmployerJobAsync(employer);
        await _api.CreateEmployerJobAsync(employer, j => j.Status = JobOfferStatus.Draft);

        var company = await GetOwnAsync(employer);

        Assert.Equal(2, company.ActiveJobsCount);
    }

    [Fact]
    public async Task Get_EachEmployerSeesOnlyOwnCompany()
    {
        var first = await _api.RegisterEmployerAsync();
        var second = await _api.RegisterEmployerAsync();

        Assert.Equal(first.CompanyId, (await GetOwnAsync(first)).Id);
        Assert.Equal(second.CompanyId, (await GetOwnAsync(second)).Id);
    }

    [Fact]
    public async Task Get_AfterApproval_ShowsApprovedStatus()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.ApproveCompanyAsync(employer.CompanyId);

        var company = await GetOwnAsync(employer);

        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.NotNull(company.ReviewedAt);
    }

    // --- edycja ---

    [Fact]
    public async Task Put_UpdatesProfileData()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);
        var newName = ApiFixture.UniqueName("Nowa nazwa");

        var response = await client.PutJsonAsync(Url, Form(newName, d =>
        {
            d.Description = "Zaktualizowany opis";
            d.Website = "https://nowa.example.com";
            d.Location = "Poznań";
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(employer.CompanyId, company.Id);
        Assert.Equal(newName, company.Name);
        Assert.Equal("Zaktualizowany opis", company.Description);
        Assert.Equal("https://nowa.example.com", company.Website);
        Assert.Equal("Poznań", company.Location);
        Assert.Equal(CompanyStatus.Pending, company.Status);
        var persisted = await GetOwnAsync(employer);
        Assert.Equal(newName, persisted.Name);
        Assert.Equal("Poznań", persisted.Location);
    }

    [Fact]
    public async Task Put_NormalizesNameAndClearsEmptyOptionalFields()
    {
        var employer = await _api.RegisterEmployerAsync(customize: d => d.CompanyDescription = "Stary opis");
        using var client = _api.CreateClient(employer);
        var name = ApiFixture.UniqueName("Czysta nazwa");

        var response = await client.PutJsonAsync(Url, Form("  " + name.Replace(" ", "   ") + "  ", d =>
        {
            d.Description = "   ";
            d.Website = "";
            d.Location = " ";
        }));

        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(name, company.Name);
        Assert.Null(company.Description);
        Assert.Null(company.Website);
        Assert.Null(company.Location);
    }

    [Fact]
    public async Task Put_OnRejectedCompany_ResubmitsItAndClearsReasonAndReviewDate()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId, "Brak opisu");
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName, d => d.Description = "Teraz mamy opis"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Pending, company.Status);
        Assert.Null(company.RejectionReason);
        Assert.Null(company.ReviewedAt);
        Assert.Equal("Teraz mamy opis", company.Description);
        var own = await GetOwnAsync(employer);
        Assert.Equal(CompanyStatus.Pending, own.Status);
        Assert.Null(own.RejectionReason);
    }

    [Fact]
    public async Task Put_OnRejectedCompanyWithoutChanges_StillResubmits()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId);
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName));

        Assert.Equal(CompanyStatus.Pending, (await response.ReadJsonAsync<CompanyDto>()).Status);
    }

    [Fact]
    public async Task Put_OnRejectedCompany_ReappearsAsPendingForAdminWhoCanApproveAgain()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId);
        using var client = _api.CreateClient(employer);
        await client.PutJsonAsync(Url, Form(employer.CompanyName));
        using var admin = await _api.AdminClientAsync();

        var list = await admin.GetAsync($"/api/admin/companies?status=0&searchTerm={Uri.EscapeDataString(employer.CompanyName)}");
        var pending = await list.ReadJsonAsync<CompanyDto[]>();
        await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.Equal(employer.CompanyId, Assert.Single(pending).Id);
        Assert.Equal(CompanyStatus.Approved, (await GetOwnAsync(employer)).Status);
    }

    [Fact]
    public async Task Put_OnPendingCompany_KeepsPendingStatus()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName, d => d.Location = "Gdynia"));

        Assert.Equal(CompanyStatus.Pending, (await response.ReadJsonAsync<CompanyDto>()).Status);
    }

    [Fact]
    public async Task Put_OnApprovedCompany_KeepsApprovedStatusAndReviewDate()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var before = await GetOwnAsync(employer);
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName, d => d.Description = "Nowy opis"));

        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.Equal(before.ReviewedAt, company.ReviewedAt);
        Assert.Equal("Nowy opis", company.Description);
    }

    [Fact]
    public async Task Put_WithNameOfAnotherCompany_ReturnsBadRequestInAnyCaseOrSpacing()
    {
        var other = await _api.CreateCompanyAsync();
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        foreach (var name in new[] { other.Name, other.Name.ToUpperInvariant(), "  " + other.Name.Replace(" ", "   ") + " " })
        {
            var response = await client.PutJsonAsync(Url, Form(name));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(NameTakenMessage, await response.ReadMessageAsync());
        }

        Assert.Equal(employer.CompanyName, (await GetOwnAsync(employer)).Name);
    }

    [Fact]
    public async Task Put_WithNameOfAnotherPendingCompany_ReturnsBadRequest()
    {
        var other = await _api.RegisterEmployerAsync();
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(other.CompanyName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Put_RejectedCompanyWithTakenName_ReturnsBadRequestAndStaysRejected()
    {
        var other = await _api.CreateCompanyAsync();
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId, "Powód");
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(other.Name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var own = await GetOwnAsync(employer);
        Assert.Equal(CompanyStatus.Rejected, own.Status);
        Assert.Equal("Powód", own.RejectionReason);
    }

    [Fact]
    public async Task Put_WithOwnNameInDifferentCase_Succeeds()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName.ToUpperInvariant()));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_ReleasedOldNameCanBeTakenByAnotherCompany()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);
        await client.PutJsonAsync(Url, Form(ApiFixture.UniqueName("Inna nazwa")));

        var other = await _api.CreateCompanyAsync(employer.CompanyName);

        Assert.Equal(employer.CompanyName, other.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Put_WithoutName_ReturnsValidationError(string name)
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Name", await response.ReadValidationFieldsAsync());
    }

    [Theory]
    [InlineData("ftp://example.com")]
    [InlineData("example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("http://")]
    [InlineData("https://exa mple.com")]
    public async Task Put_WithInvalidWebsite_ReturnsValidationErrorAndKeepsProfile(string website)
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName, d => d.Website = website));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Website", await response.ReadValidationFieldsAsync());
        Assert.Null((await GetOwnAsync(employer)).Website);
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("HTTPS://Example.com/Path?q=1#frag")]
    public async Task Put_WithHttpOrHttpsWebsite_Succeeds(string website)
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, Form(employer.CompanyName, d => d.Website = website));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(website, (await response.ReadJsonAsync<CompanyDto>()).Website);
    }

    [Fact]
    public async Task Put_WithTooLongFields_ReturnsValidationError()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, new CompanyFormDto
        {
            Name = new string('n', 201),
            Description = new string('d', 5001),
            Location = new string('l', 201)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await response.ReadValidationFieldsAsync();
        Assert.Contains("Name", fields);
        Assert.Contains("Description", fields);
        Assert.Contains("Location", fields);
    }

    [Fact]
    public async Task Put_AcceptsFieldsAtMaximumLength()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PutJsonAsync(Url, new CompanyFormDto
        {
            Name = new string('N', 190) + Guid.NewGuid().ToString("N")[..10],
            Description = new string('d', 5000),
            Location = new string('l', 200)
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Put_DoesNotChangeOtherCompanies()
    {
        var first = await _api.RegisterEmployerAsync();
        var second = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(first);

        await client.PutJsonAsync(Url, Form(ApiFixture.UniqueName("Pierwsza zmieniona"), d => d.Location = "Zmieniona"));

        var untouched = await GetOwnAsync(second);
        Assert.Equal(second.CompanyName, untouched.Name);
        Assert.Null(untouched.Location);
    }

    [Fact]
    public async Task Put_IgnoresStatusSuppliedInBody()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);
        var json = $$"""{"name":"{{employer.CompanyName}}","status":1,"rejectionReason":"x","id":1}""";

        var response = await client.PutRawJsonAsync(Url, json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Pending, company.Status);
        Assert.Equal(employer.CompanyId, company.Id);
    }

    [Fact]
    public async Task Put_ApprovedCompanyRename_IsVisibleInPublicProfileAndJobs()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        var job = await _api.CreateEmployerJobAsync(employer);
        var newName = ApiFixture.UniqueName("Przemianowana");
        using var client = _api.CreateClient(employer);

        await client.PutJsonAsync(Url, Form(newName));

        using var anonymous = _api.CreateClient();
        var company = await (await anonymous.GetAsync($"/api/companies/{employer.CompanyId}")).ReadJsonAsync<CompanyDto>();
        var publicJob = await (await anonymous.GetAsync($"/api/jobs/{job.Id}")).ReadJsonAsync<JobOfferDto>();
        Assert.Equal(newName, company.Name);
        Assert.Equal(newName, publicJob.Company);
    }
}
