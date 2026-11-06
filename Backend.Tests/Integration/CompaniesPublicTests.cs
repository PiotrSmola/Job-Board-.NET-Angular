using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Publiczne /api/companies i /api/companies/{id}: tylko firmy zatwierdzone (pozostałe widzą admin i członkowie firmy)
[Collection(ApiCollection.Name)]
public class CompaniesPublicTests
{
    private const string Url = "/api/companies";

    private readonly ApiFixture _api;

    public CompaniesPublicTests(ApiFixture api) => _api = api;

    private async Task<(HttpResponseMessage Response, CompanyDto[] Items)> ListAsync(string query, HttpClient? client = null)
    {
        var own = client == null;
        client ??= _api.CreateClient();
        try
        {
            var response = await client.GetAsync(Url + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (response, await response.ReadJsonAsync<CompanyDto[]>());
        }
        finally
        {
            if (own) client.Dispose();
        }
    }

    private static string Q(string prefix, string extra = "") =>
        $"?searchTerm={Uri.EscapeDataString(prefix)}{extra}";

    // --- lista ---

    [Fact]
    public async Task List_IsPubliclyAccessible_AndIncludesSeededCompanies()
    {
        var (totalResponse, _) = await ListAsync("?pageSize=1");
        var (_, items) = await ListAsync("?pageSize=50&searchTerm=techcorp");

        Assert.True(totalResponse.ReadPagination().TotalItems >= 20, "Seed contains at least 20 approved companies");
        Assert.Contains(items, c => c.Name == "TechCorp Solutions");
    }

    [Fact]
    public async Task List_ReturnsOnlyApprovedCompanies()
    {
        var prefix = ApiFixture.UniqueName("Publiczna");
        var approved = await _api.CreateCompanyAsync(prefix + " approved");
        await _api.RegisterEmployerAsync(prefix + " pending");
        var rejected = await _api.RegisterEmployerAsync(prefix + " rejected");
        await _api.RejectCompanyAsync(rejected.CompanyId);

        var (_, items) = await ListAsync(Q(prefix));

        Assert.Equal(approved.Id, Assert.Single(items).Id);
    }

    [Fact]
    public async Task List_AfterApprovalCompanyAppears_AfterRejectionDisappears()
    {
        var employer = await _api.RegisterEmployerAsync();
        Assert.Empty((await ListAsync(Q(employer.CompanyName))).Items);

        await _api.ApproveCompanyAsync(employer.CompanyId);
        Assert.Single((await ListAsync(Q(employer.CompanyName))).Items);

        await _api.RejectCompanyAsync(employer.CompanyId);
        Assert.Empty((await ListAsync(Q(employer.CompanyName))).Items);
    }

    [Fact]
    public async Task List_HidesReviewFieldsAndMembersCount()
    {
        var company = await _api.CreateCompanyAsync(customize: c => c.Description = "Opis publiczny");

        var (response, items) = await ListAsync(Q(company.Name));

        var dto = Assert.Single(items);
        Assert.Equal("Opis publiczny", dto.Description);
        Assert.Null(dto.Status);
        Assert.Null(dto.RejectionReason);
        Assert.Null(dto.ReviewedAt);
        Assert.Null(dto.MembersCount);
        using var raw = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var first = raw.RootElement[0];
        Assert.Equal(JsonValueKind.Null, first.GetProperty("status").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("rejectionReason").ValueKind);
        Assert.Equal(JsonValueKind.Null, first.GetProperty("reviewedAt").ValueKind);
    }

    [Fact]
    public async Task List_AsAdminOrLoggedUser_StillHidesReviewFieldsAndNonApprovedCompanies()
    {
        var prefix = ApiFixture.UniqueName("Lista");
        await _api.CreateCompanyAsync(prefix + " ok");
        await _api.RegisterEmployerAsync(prefix + " pending");
        using var admin = await _api.AdminClientAsync();
        using var user = await _api.UserClientAsync();

        var (_, adminItems) = await ListAsync(Q(prefix), admin);
        var (_, userItems) = await ListAsync(Q(prefix), user);

        foreach (var items in new[] { adminItems, userItems })
        {
            var dto = Assert.Single(items);
            Assert.Null(dto.Status);
            Assert.Null(dto.MembersCount);
        }
    }

    [Fact]
    public async Task List_PaginatesAndReportsPaginationHeader()
    {
        var prefix = ApiFixture.UniqueName("Strony");
        for (var i = 0; i < 7; i++)
        {
            await _api.CreateCompanyAsync($"{prefix} {i}");
        }

        var (page1, items1) = await ListAsync(Q(prefix, "&pageSize=3"));
        var (page3, items3) = await ListAsync(Q(prefix, "&pageSize=3&pageNumber=3"));
        var (page4, items4) = await ListAsync(Q(prefix, "&pageSize=3&pageNumber=4"));

        var pagination = page1.ReadPagination();
        Assert.Equal(1, pagination.CurrentPage);
        Assert.Equal(3, pagination.ItemsPerPage);
        Assert.Equal(7, pagination.TotalItems);
        Assert.Equal(3, pagination.TotalPages);
        Assert.Equal(3, items1.Length);
        Assert.Equal(3, page3.ReadPagination().CurrentPage);
        Assert.Equal(new[] { $"{prefix} 6" }, items3.Select(c => c.Name));
        Assert.Empty(items4);
        Assert.Equal(7, page4.ReadPagination().TotalItems);
    }

    [Fact]
    public async Task List_DefaultsToPageOfTen_CapsAtFifty_AndFallsBackOnInvalidValues()
    {
        var (defaultResponse, defaultItems) = await ListAsync(string.Empty);
        var (hugeResponse, _) = await ListAsync("?pageSize=500");
        var (zeroResponse, _) = await ListAsync("?pageSize=0&pageNumber=0");

        Assert.Equal(10, defaultResponse.ReadPagination().ItemsPerPage);
        Assert.Equal(10, defaultItems.Length);
        Assert.Equal(50, hugeResponse.ReadPagination().ItemsPerPage);
        var zero = zeroResponse.ReadPagination();
        Assert.Equal(10, zero.ItemsPerPage);
        Assert.Equal(1, zero.CurrentPage);
    }

    [Fact]
    public async Task List_SearchTerm_MatchesNameAndLocationCaseInsensitivelyAndIgnoresPadding()
    {
        var prefix = ApiFixture.UniqueName("Szukaj");
        var byName = await _api.CreateCompanyAsync(prefix + " Alfa");
        var place = ApiFixture.UniqueName("Miejscowosc");
        var byLocation = await _api.CreateCompanyAsync(ApiFixture.UniqueName("Beta"), c => c.Location = place);

        var (_, nameItems) = await ListAsync(Q("  " + prefix.ToUpperInvariant() + "  "));
        var (_, locationItems) = await ListAsync(Q(place.ToLowerInvariant()));
        var (_, none) = await ListAsync(Q(ApiFixture.UniqueName("Nieistnieje")));

        Assert.Equal(byName.Id, Assert.Single(nameItems).Id);
        Assert.Equal(byLocation.Id, Assert.Single(locationItems).Id);
        Assert.Empty(none);
    }

    [Fact]
    public async Task List_SearchTerm_DoesNotMatchDescriptionOrWebsite()
    {
        var token = ApiFixture.UniqueName("Tylkoopis").Replace(" ", string.Empty);
        await _api.CreateCompanyAsync(customize: c => c.Description = token);

        var (_, items) = await ListAsync(Q(token));

        Assert.Empty(items);
    }

    [Fact]
    public async Task List_OrderBy_SortsByNameAscendingByDefaultAndDescending()
    {
        var prefix = ApiFixture.UniqueName("Kolejnosc");
        foreach (var suffix in new[] { "B", "C", "A" })
        {
            await _api.CreateCompanyAsync($"{prefix} {suffix}");
        }

        var (_, byDefault) = await ListAsync(Q(prefix));
        var (_, asc) = await ListAsync(Q(prefix, "&orderBy=nameAsc"));
        var (_, desc) = await ListAsync(Q(prefix, "&orderBy=nameDesc"));
        var (_, unknown) = await ListAsync(Q(prefix, "&orderBy=bogus"));

        var ascending = new[] { $"{prefix} A", $"{prefix} B", $"{prefix} C" };
        Assert.Equal(ascending, byDefault.Select(c => c.Name));
        Assert.Equal(ascending, asc.Select(c => c.Name));
        Assert.Equal(ascending.Reverse(), desc.Select(c => c.Name));
        Assert.Equal(ascending, unknown.Select(c => c.Name));
    }

    [Fact]
    public async Task List_OrderByJobsDesc_SortsByActiveJobsThenByName()
    {
        var prefix = ApiFixture.UniqueName("Oferty");
        var none = await _api.CreateCompanyAsync($"{prefix} A none");
        var one = await _api.CreateCompanyAsync($"{prefix} B one");
        var three = await _api.CreateCompanyAsync($"{prefix} C three");
        var oneToo = await _api.CreateCompanyAsync($"{prefix} A one");
        for (var i = 0; i < 3; i++) await _api.CreateJobAsync(j => j.CompanyId = three.Id);
        await _api.CreateJobAsync(j => j.CompanyId = one.Id);
        await _api.CreateJobAsync(j => j.CompanyId = oneToo.Id);
        // oferty, które się nie liczą, nie mogą zmienić kolejności
        for (var i = 0; i < 5; i++) await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = none.Id);

        var (_, items) = await ListAsync(Q(prefix, "&orderBy=jobsDesc"));

        Assert.Equal(
            new[] { $"{prefix} C three", $"{prefix} A one", $"{prefix} B one", $"{prefix} A none" },
            items.Select(c => c.Name));
        Assert.Equal(new[] { 3, 1, 1, 0 }, items.Select(c => c.ActiveJobsCount));
    }

    [Fact]
    public async Task List_ActiveJobsCount_CountsOnlyPubliclyVisibleOffers()
    {
        var company = await _api.CreateCompanyAsync();
        await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobAsync(j =>
        {
            j.CompanyId = company.Id;
            j.ExpiresAt = DateTime.UtcNow.AddDays(10);
        });
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, customize: j => j.CompanyId = company.Id);
        await _api.CreateExpiredJobAsync(j => j.CompanyId = company.Id);

        var (_, items) = await ListAsync(Q(company.Name));

        Assert.Equal(2, Assert.Single(items).ActiveJobsCount);
    }

    [Fact]
    public async Task List_ActiveJobsCount_MatchesNumberOfCompanyOffersInPublicJobList()
    {
        var company = await _api.CreateCompanyAsync();
        for (var i = 0; i < 4; i++) await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, customize: j => j.CompanyId = company.Id);
        using var anonymous = _api.CreateClient();

        var jobs = await anonymous.GetAsync($"/api/jobs?companyId={company.Id}");
        var (_, items) = await ListAsync(Q(company.Name));

        Assert.Equal(jobs.ReadPagination().TotalItems, Assert.Single(items).ActiveJobsCount);
    }

    // --- szczegóły ---

    [Fact]
    public async Task Get_ApprovedCompany_IsPublicWithoutReviewFields()
    {
        var company = await _api.CreateCompanyAsync(customize: c =>
        {
            c.Description = "Opis";
            c.Website = "https://firma.example.com";
            c.Location = "Kraków";
        });
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"{Url}/{company.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(company.Id, dto.Id);
        Assert.Equal(company.Name, dto.Name);
        Assert.Equal("Opis", dto.Description);
        Assert.Equal("https://firma.example.com", dto.Website);
        Assert.Equal("Kraków", dto.Location);
        Assert.Null(dto.Status);
        Assert.Null(dto.RejectionReason);
        Assert.Null(dto.ReviewedAt);
        Assert.Null(dto.MembersCount);
    }

    [Fact]
    public async Task Get_ApprovedCompany_ReportsActiveJobsCount()
    {
        var company = await _api.CreateCompanyAsync();
        await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = company.Id);
        using var anonymous = _api.CreateClient();

        var dto = await (await anonymous.GetAsync($"{Url}/{company.Id}")).ReadJsonAsync<CompanyDto>();

        Assert.Equal(2, dto.ActiveJobsCount);
    }

    [Fact]
    public async Task Get_ApprovedCompany_AsRegularUser_HidesReviewFields()
    {
        var company = await _api.CreateCompanyAsync();
        using var user = await _api.UserClientAsync();

        var dto = await (await user.GetAsync($"{Url}/{company.Id}")).ReadJsonAsync<CompanyDto>();

        Assert.Null(dto.Status);
    }

    [Theory]
    [InlineData(CompanyStatus.Pending)]
    [InlineData(CompanyStatus.Rejected)]
    public async Task Get_NonApprovedCompany_ReturnsNotFoundForAnonymousUserAndOtherEmployer(CompanyStatus status)
    {
        var employer = await _api.RegisterEmployerAsync();
        if (status == CompanyStatus.Rejected)
        {
            await _api.RejectCompanyAsync(employer.CompanyId, "Powód");
        }

        var other = await _api.RegisterApprovedEmployerAsync();
        using var anonymous = _api.CreateClient();
        using var user = await _api.UserClientAsync();
        using var otherEmployer = _api.CreateClient(other);

        foreach (var client in new[] { anonymous, user, otherEmployer })
        {
            var response = await client.GetAsync($"{Url}/{employer.CompanyId}");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal("Company not found", await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Get_NonApprovedCompany_ReturnsNotFoundAlsoToOtherPendingEmployer()
    {
        var first = await _api.RegisterEmployerAsync();
        var second = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(second);

        var response = await client.GetAsync($"{Url}/{first.CompanyId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task Get_PendingCompany_AsAdmin_ReturnsFullDetails()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync($"{Url}/{employer.CompanyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Pending, dto.Status);
        Assert.Equal(employer.CompanyName, dto.Name);
    }

    [Fact]
    public async Task Get_RejectedCompany_AsAdminAndMember_ReturnsReason()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId, "Powód odrzucenia");
        using var admin = await _api.AdminClientAsync();
        using var member = _api.CreateClient(employer);

        foreach (var client in new[] { admin, member })
        {
            var response = await client.GetAsync($"{Url}/{employer.CompanyId}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var dto = await response.ReadJsonAsync<CompanyDto>();
            Assert.Equal(CompanyStatus.Rejected, dto.Status);
            Assert.Equal("Powód odrzucenia", dto.RejectionReason);
            Assert.NotNull(dto.ReviewedAt);
        }
    }

    [Fact]
    public async Task Get_PendingCompany_AsMember_ReturnsStatus()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var member = _api.CreateClient(employer);

        var response = await member.GetAsync($"{Url}/{employer.CompanyId}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(CompanyStatus.Pending, (await response.ReadJsonAsync<CompanyDto>()).Status);
    }

    [Fact]
    public async Task Get_ApprovedCompany_AsMember_ReturnsStatus()
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var member = _api.CreateClient(employer);

        var dto = await (await member.GetAsync($"{Url}/{employer.CompanyId}")).ReadJsonAsync<CompanyDto>();

        Assert.Equal(CompanyStatus.Approved, dto.Status);
    }

    [Fact]
    public async Task Get_MissingCompany_ReturnsNotFound()
    {
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"{Url}/{int.MaxValue}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Company not found", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Get_WithNonNumericId_ReturnsNotFound()
    {
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"{Url}/abc");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}
