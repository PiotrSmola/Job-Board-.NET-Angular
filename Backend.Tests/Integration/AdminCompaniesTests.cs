using System.Net;
using System.Net.Http.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// /api/admin/companies: moderacja firm (lista, dodawanie, edycja, zatwierdzanie, odrzucanie, usuwanie)
[Collection(ApiCollection.Name)]
public class AdminCompaniesTests
{
    private const string Url = "/api/admin/companies";
    private const string NameTakenMessage = "Company name is already taken";

    private readonly ApiFixture _api;

    public AdminCompaniesTests(ApiFixture api) => _api = api;

    private async Task<(HttpResponseMessage Response, CompanyDto[] Items)> ListAsync(string query)
    {
        using var admin = await _api.AdminClientAsync();
        var response = await admin.GetAsync(Url + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, await response.ReadJsonAsync<CompanyDto[]>());
    }

    private static string Q(string prefix, string extra = "") =>
        $"?searchTerm={Uri.EscapeDataString(prefix)}{extra}";

    // --- autoryzacja ---

    public static IEnumerable<object[]> Endpoints() => new[]
    {
        new object[] { "GET", "" },
        new object[] { "POST", "" },
        new object[] { "PUT", "/1" },
        new object[] { "POST", "/1/approve" },
        new object[] { "POST", "/1/reject" },
        new object[] { "DELETE", "/1" }
    };

    private static HttpRequestMessage Request(string method, string path)
    {
        var request = new HttpRequestMessage(new HttpMethod(method), Url + path);
        if (method is "POST" or "PUT")
        {
            request.Content = JsonContent.Create(
                path.EndsWith("reject") ? new RejectCompanyDto { Reason = "x" } : (object)new CompanyFormDto { Name = "x" },
                options: ApiJson.Options);
        }

        return request;
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_WithoutToken_ReturnsUnauthorized(string method, string path)
    {
        using var client = _api.CreateClient();

        var response = await client.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_AsRegularUser_ReturnsForbidden(string method, string path)
    {
        using var client = await _api.UserClientAsync();

        var response = await client.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(Endpoints))]
    public async Task Endpoint_AsEmployer_ReturnsForbidden(string method, string path)
    {
        var employer = await _api.RegisterApprovedEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.SendAsync(Request(method, path));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Employer_CannotApproveOwnCompany()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient(employer);

        var response = await client.PostAsync($"{Url}/{employer.CompanyId}/approve", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var own = _api.CreateClient(employer);
        var company = await (await own.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Pending, company.Status);
    }

    // --- lista ---

    [Fact]
    public async Task List_ReturnsAllStatusesWithReviewFieldsAndMembersCount()
    {
        var prefix = ApiFixture.UniqueName("Lista");
        var pending = await _api.RegisterEmployerAsync(prefix + " pending");
        var approved = await _api.CreateCompanyAsync(prefix + " approved");
        var rejected = await _api.RegisterEmployerAsync(prefix + " rejected");
        await _api.RejectCompanyAsync(rejected.CompanyId, "Za mało danych");

        var (_, items) = await ListAsync(Q(prefix));

        Assert.Equal(3, items.Length);
        var pendingDto = items.Single(c => c.Id == pending.CompanyId);
        Assert.Equal(CompanyStatus.Pending, pendingDto.Status);
        Assert.Equal(1, pendingDto.MembersCount);
        var approvedDto = items.Single(c => c.Id == approved.Id);
        Assert.Equal(CompanyStatus.Approved, approvedDto.Status);
        Assert.Equal(0, approvedDto.MembersCount);
        Assert.NotNull(approvedDto.ReviewedAt);
        var rejectedDto = items.Single(c => c.Id == rejected.CompanyId);
        Assert.Equal(CompanyStatus.Rejected, rejectedDto.Status);
        Assert.Equal("Za mało danych", rejectedDto.RejectionReason);
    }

    [Fact]
    public async Task List_MembersCountCountsAllEmployersOfCompany()
    {
        var company = await _api.RegisterEmployerAsync();
        await _api.QueryDbAsync(async db =>
        {
            // drugie konto pracodawcy w tej samej firmie (zespół)
            var owner = await db.Users.SingleAsync(u => u.Id == company.Id);
            db.Users.Add(new ApplicationUser
            {
                UserName = $"team_{Guid.NewGuid():N}@test.local",
                Email = $"team_{Guid.NewGuid():N}@test.local",
                CompanyId = owner.CompanyId
            });
            await db.SaveChangesAsync();
            return 0;
        });

        var (_, items) = await ListAsync(Q(company.CompanyName));

        Assert.Equal(2, Assert.Single(items).MembersCount);
    }

    [Fact]
    public async Task List_PaginatesAndReportsPaginationHeader()
    {
        var prefix = ApiFixture.UniqueName("Strony");
        for (var i = 0; i < 12; i++)
        {
            await _api.CreateCompanyAsync($"{prefix} {i:D2}");
        }

        var (first, firstItems) = await ListAsync(Q(prefix, "&pageSize=5&orderBy=nameAsc"));
        var (third, thirdItems) = await ListAsync(Q(prefix, "&pageSize=5&pageNumber=3&orderBy=nameAsc"));

        var firstPagination = first.ReadPagination();
        Assert.Equal(1, firstPagination.CurrentPage);
        Assert.Equal(5, firstPagination.ItemsPerPage);
        Assert.Equal(12, firstPagination.TotalItems);
        Assert.Equal(3, firstPagination.TotalPages);
        Assert.Equal(5, firstItems.Length);
        Assert.Equal(3, third.ReadPagination().CurrentPage);
        Assert.Equal(new[] { $"{prefix} 10", $"{prefix} 11" }, thirdItems.Select(c => c.Name).ToArray());
    }

    [Fact]
    public async Task List_DefaultsToPageOfTen_AndCapsPageSizeAtFifty()
    {
        var (defaultResponse, _) = await ListAsync(string.Empty);
        var (hugeResponse, _) = await ListAsync("?pageSize=500");

        Assert.Equal(10, defaultResponse.ReadPagination().ItemsPerPage);
        Assert.Equal(50, hugeResponse.ReadPagination().ItemsPerPage);
    }

    [Fact]
    public async Task List_SearchTerm_MatchesNameAndLocationCaseInsensitively()
    {
        var prefix = ApiFixture.UniqueName("Szukaj");
        var byName = await _api.CreateCompanyAsync(prefix + " Alfa");
        var location = ApiFixture.UniqueName("Miasto");
        var byLocation = await _api.CreateCompanyAsync(ApiFixture.UniqueName("Beta"), c => c.Location = location);

        var (_, nameItems) = await ListAsync(Q(prefix.ToUpperInvariant()));
        var (_, locationItems) = await ListAsync(Q(location.ToLowerInvariant()));

        Assert.Equal(byName.Id, Assert.Single(nameItems).Id);
        Assert.Equal(byLocation.Id, Assert.Single(locationItems).Id);
    }

    [Fact]
    public async Task List_FiltersByStatus()
    {
        var prefix = ApiFixture.UniqueName("Status");
        var pending = await _api.RegisterEmployerAsync(prefix + " p");
        var approved = await _api.CreateCompanyAsync(prefix + " a");
        var rejected = await _api.RegisterEmployerAsync(prefix + " r");
        await _api.RejectCompanyAsync(rejected.CompanyId);

        var (_, pendingItems) = await ListAsync(Q(prefix, "&status=0"));
        var (_, approvedItems) = await ListAsync(Q(prefix, "&status=1"));
        var (_, rejectedItems) = await ListAsync(Q(prefix, "&status=2"));
        var (_, allItems) = await ListAsync(Q(prefix));

        Assert.Equal(pending.CompanyId, Assert.Single(pendingItems).Id);
        Assert.Equal(approved.Id, Assert.Single(approvedItems).Id);
        Assert.Equal(rejected.CompanyId, Assert.Single(rejectedItems).Id);
        Assert.Equal(3, allItems.Length);
    }

    [Fact]
    public async Task List_WithUnknownStatusValue_ReturnsBadRequest()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.GetAsync(Url + "?status=7");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task List_OrderBy_SortsByCreationDateAndName()
    {
        var prefix = ApiFixture.UniqueName("Sort");
        var names = new[] { $"{prefix} B", $"{prefix} C", $"{prefix} A" };
        foreach (var name in names)
        {
            await _api.CreateCompanyAsync(name);
        }

        var (_, newest) = await ListAsync(Q(prefix));
        var (_, newestExplicit) = await ListAsync(Q(prefix, "&orderBy=newest"));
        var (_, oldest) = await ListAsync(Q(prefix, "&orderBy=oldest"));
        var (_, nameAsc) = await ListAsync(Q(prefix, "&orderBy=nameAsc"));
        var (_, nameDesc) = await ListAsync(Q(prefix, "&orderBy=nameDesc"));
        var (_, unknown) = await ListAsync(Q(prefix, "&orderBy=bogus"));

        Assert.Equal(names.Reverse(), newest.Select(c => c.Name));
        Assert.Equal(names.Reverse(), newestExplicit.Select(c => c.Name));
        Assert.Equal(names, oldest.Select(c => c.Name));
        Assert.Equal(new[] { $"{prefix} A", $"{prefix} B", $"{prefix} C" }, nameAsc.Select(c => c.Name));
        Assert.Equal(new[] { $"{prefix} C", $"{prefix} B", $"{prefix} A" }, nameDesc.Select(c => c.Name));
        Assert.Equal(names.Reverse(), unknown.Select(c => c.Name));
    }

    [Fact]
    public async Task List_ActiveJobsCount_CountsOnlyPublishedOffers()
    {
        var company = await _api.CreateCompanyAsync();
        await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = company.Id);
        await _api.CreateJobWithStatusAsync(JobOfferStatus.Closed, customize: j => j.CompanyId = company.Id);
        await _api.CreateExpiredJobAsync(j => j.CompanyId = company.Id);

        var (_, items) = await ListAsync(Q(company.Name));

        Assert.Equal(1, Assert.Single(items).ActiveJobsCount);
    }

    // --- dodawanie ---

    [Fact]
    public async Task Create_ReturnsCreatedApprovedCompanyWithLocation()
    {
        using var admin = await _api.AdminClientAsync();
        var dto = new CompanyFormDto
        {
            Name = ApiFixture.UniqueName("Nowa"),
            Description = "Opis",
            Website = "https://nowa.example.com",
            Location = "Gdańsk"
        };

        var response = await admin.PostJsonAsync(Url, dto);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.True(company.Id > 0);
        Assert.EndsWith($"/api/Companies/{company.Id}", response.Headers.Location?.ToString());
        Assert.Equal(dto.Name, company.Name);
        Assert.Equal("Opis", company.Description);
        Assert.Equal("https://nowa.example.com", company.Website);
        Assert.Equal("Gdańsk", company.Location);
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.NotNull(company.ReviewedAt);
        Assert.Null(company.RejectionReason);
        Assert.Equal(0, company.MembersCount);
        Assert.Equal(0, company.ActiveJobsCount);
    }

    [Fact]
    public async Task Create_RecordsAdminAsReviewer()
    {
        var company = await _api.CreateCompanyAsync();

        var reviewer = await _api.QueryDbAsync(async db =>
            (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == company.Id)).ReviewedByUserId);
        var adminId = await _api.QueryDbAsync(db =>
            db.Users.AsNoTracking().Where(u => u.Email == TestUsers.AdminEmail).Select(u => u.Id).SingleAsync());
        Assert.Equal(adminId, reviewer);
    }

    [Fact]
    public async Task Create_IsImmediatelyVisibleInPublicList()
    {
        var company = await _api.CreateCompanyAsync();
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"/api/companies/{company.Id}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Create_NormalizesNameAndEmptyOptionalFields()
    {
        var unique = ApiFixture.UniqueName("Norm");
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync(Url, new CompanyFormDto
        {
            Name = "  " + unique.Replace(" ", "   ") + " ",
            Description = " ",
            Website = "",
            Location = "  Łódź  "
        });

        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(unique, company.Name);
        Assert.Null(company.Description);
        Assert.Null(company.Website);
        Assert.Equal("Łódź", company.Location);
    }

    [Fact]
    public async Task Create_WithTakenName_ReturnsBadRequest()
    {
        var existing = await _api.CreateCompanyAsync();
        using var admin = await _api.AdminClientAsync();

        var exact = await admin.PostJsonAsync(Url, new CompanyFormDto { Name = existing.Name });
        var otherCase = await admin.PostJsonAsync(Url, new CompanyFormDto { Name = existing.Name.ToUpperInvariant() });
        var spaces = await admin.PostJsonAsync(Url, new CompanyFormDto { Name = "  " + existing.Name.Replace(" ", "  ") + "  " });

        foreach (var response in new[] { exact, otherCase, spaces })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(NameTakenMessage, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Create_WithNameOfPendingCompany_ReturnsBadRequest()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync(Url, new CompanyFormDto { Name = employer.CompanyName });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await response.ReadMessageAsync());
    }

    [Theory]
    [InlineData("", null, "Name")]
    [InlineData("   ", null, "Name")]
    [InlineData("ftp://x.example.com", "Website", "Website")]
    [InlineData("nie-adres", "Website", "Website")]
    public async Task Create_WithInvalidData_ReturnsValidationError(string value, string? field, string expectedErrorField)
    {
        using var admin = await _api.AdminClientAsync();
        var dto = new CompanyFormDto { Name = ApiFixture.UniqueName("Walidacja") };
        if (field == "Website")
        {
            dto.Website = value;
        }
        else
        {
            dto.Name = value;
        }

        var response = await admin.PostJsonAsync(Url, dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(expectedErrorField, await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Create_WithTooLongFields_ReturnsValidationError()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync(Url, new CompanyFormDto
        {
            Name = new string('n', 201),
            Description = new string('d', 5001),
            Website = "https://example.com/" + new string('w', 300),
            Location = new string('l', 201)
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await response.ReadValidationFieldsAsync();
        Assert.Contains("Name", fields);
        Assert.Contains("Description", fields);
        Assert.Contains("Website", fields);
        Assert.Contains("Location", fields);
    }

    // --- edycja ---

    [Fact]
    public async Task Update_ChangesDataAndKeepsStatusOfPendingCompany()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();
        var newName = ApiFixture.UniqueName("Zmieniona");

        var response = await admin.PutJsonAsync($"{Url}/{employer.CompanyId}", new CompanyFormDto
        {
            Name = newName,
            Description = "Nowy opis",
            Website = "https://zmieniona.example.com",
            Location = "Sopot"
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(employer.CompanyId, company.Id);
        Assert.Equal(newName, company.Name);
        Assert.Equal("Nowy opis", company.Description);
        Assert.Equal("Sopot", company.Location);
        Assert.Equal(CompanyStatus.Pending, company.Status);
        Assert.Equal(1, company.MembersCount);
    }

    [Fact]
    public async Task Update_KeepsRejectedStatusAndReason()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId, "Powód");
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"{Url}/{employer.CompanyId}", new CompanyFormDto { Name = employer.CompanyName, Location = "Opole" });

        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Rejected, company.Status);
        Assert.Equal("Powód", company.RejectionReason);
    }

    [Fact]
    public async Task Update_WithOwnNameInDifferentCase_Succeeds()
    {
        var company = await _api.CreateCompanyAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"{Url}/{company.Id}", new CompanyFormDto { Name = company.Name.ToUpperInvariant() });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(company.Name.ToUpperInvariant(), (await response.ReadJsonAsync<CompanyDto>()).Name);
    }

    [Fact]
    public async Task Update_WithNameOfAnotherCompany_ReturnsBadRequest()
    {
        var first = await _api.CreateCompanyAsync();
        var second = await _api.CreateCompanyAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"{Url}/{second.Id}", new CompanyFormDto { Name = first.Name.ToLowerInvariant() });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Update_OfMissingCompany_ReturnsNotFound()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"{Url}/{int.MaxValue}", new CompanyFormDto { Name = ApiFixture.UniqueName("X") });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Company not found", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Update_WithInvalidData_ReturnsValidationErrorAndKeepsCompany()
    {
        var company = await _api.CreateCompanyAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PutJsonAsync($"{Url}/{company.Id}", new CompanyFormDto { Name = company.Name, Website = "gopher://x" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var anonymous = _api.CreateClient();
        var current = await (await anonymous.GetAsync($"/api/companies/{company.Id}")).ReadJsonAsync<CompanyDto>();
        Assert.Null(current.Website);
    }

    // --- zatwierdzanie ---

    [Fact]
    public async Task Approve_SetsApprovedStatusReviewedAtAndReviewer()
    {
        var employer = await _api.RegisterEmployerAsync();
        var before = DateTime.UtcNow.AddSeconds(-5);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsync($"{Url}/{employer.CompanyId}/approve", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.NotNull(company.ReviewedAt);
        Assert.True(company.ReviewedAt >= before);
        Assert.Null(company.RejectionReason);
        var adminId = await _api.QueryDbAsync(db =>
            db.Users.AsNoTracking().Where(u => u.Email == TestUsers.AdminEmail).Select(u => u.Id).SingleAsync());
        var reviewer = await _api.QueryDbAsync(async db =>
            (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == employer.CompanyId)).ReviewedByUserId);
        Assert.Equal(adminId, reviewer);
    }

    [Fact]
    public async Task Approve_RejectedCompany_ClearsRejectionReason()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId, "Powód odrzucenia");

        var company = await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.Equal(CompanyStatus.Approved, company.Status);
        Assert.Null(company.RejectionReason);
    }

    [Fact]
    public async Task Approve_MakesCompanyPublic()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/companies/{employer.CompanyId}")).StatusCode);

        await _api.ApproveCompanyAsync(employer.CompanyId);

        var response = await anonymous.GetAsync($"/api/companies/{employer.CompanyId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Approve_IsIdempotent()
    {
        var employer = await _api.RegisterEmployerAsync();

        await _api.ApproveCompanyAsync(employer.CompanyId);
        var second = await _api.ApproveCompanyAsync(employer.CompanyId);

        Assert.Equal(CompanyStatus.Approved, second.Status);
    }

    [Fact]
    public async Task Approve_OfMissingCompany_ReturnsNotFound()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostAsync($"{Url}/{int.MaxValue}/approve", null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- odrzucanie ---

    [Fact]
    public async Task Reject_SetsRejectedStatusReasonAndReviewedAt()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync($"{Url}/{employer.CompanyId}/reject", new RejectCompanyDto { Reason = "  Brak danych kontaktowych  " });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Rejected, company.Status);
        Assert.Equal("Brak danych kontaktowych", company.RejectionReason);
        Assert.NotNull(company.ReviewedAt);
        var adminId = await _api.QueryDbAsync(db =>
            db.Users.AsNoTracking().Where(u => u.Email == TestUsers.AdminEmail).Select(u => u.Id).SingleAsync());
        var reviewer = await _api.QueryDbAsync(async db =>
            (await db.Companies.AsNoTracking().SingleAsync(c => c.Id == employer.CompanyId)).ReviewedByUserId);
        Assert.Equal(adminId, reviewer);
    }

    [Fact]
    public async Task Reject_ApprovedCompany_HidesItFromPublic()
    {
        var company = await _api.CreateCompanyAsync();
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.OK, (await anonymous.GetAsync($"/api/companies/{company.Id}")).StatusCode);

        await _api.RejectCompanyAsync(company.Id);

        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/companies/{company.Id}")).StatusCode);
    }

    [Fact]
    public async Task Reject_VisibleToEmployerWithReason()
    {
        var employer = await _api.RegisterEmployerAsync();
        await _api.RejectCompanyAsync(employer.CompanyId, "Powód widoczny dla pracodawcy");
        using var client = _api.CreateClient(employer);

        var company = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();

        Assert.Equal(CompanyStatus.Rejected, company.Status);
        Assert.Equal("Powód widoczny dla pracodawcy", company.RejectionReason);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Reject_WithoutReason_ReturnsValidationError(string reason)
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync($"{Url}/{employer.CompanyId}/reject", new RejectCompanyDto { Reason = reason });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("Reason", await response.ReadValidationFieldsAsync());
        using var client = _api.CreateClient(employer);
        var company = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(CompanyStatus.Pending, company.Status);
    }

    [Fact]
    public async Task Reject_WithMissingBody_ReturnsBadRequest()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostRawJsonAsync($"{Url}/{employer.CompanyId}/reject", "{}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Reject_WithReasonOfMaxLength_SucceedsAndLongerFails()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var tooLong = await admin.PostJsonAsync($"{Url}/{employer.CompanyId}/reject", new RejectCompanyDto { Reason = new string('r', 1001) });
        var maxLength = await admin.PostJsonAsync($"{Url}/{employer.CompanyId}/reject", new RejectCompanyDto { Reason = new string('r', 1000) });

        Assert.Equal(HttpStatusCode.BadRequest, tooLong.StatusCode);
        Assert.Equal(HttpStatusCode.OK, maxLength.StatusCode);
        Assert.Equal(1000, (await maxLength.ReadJsonAsync<CompanyDto>()).RejectionReason!.Length);
    }

    [Fact]
    public async Task Reject_OfMissingCompany_ReturnsNotFound()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.PostJsonAsync($"{Url}/{int.MaxValue}/reject", new RejectCompanyDto { Reason = "x" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // --- usuwanie ---

    [Fact]
    public async Task Delete_EmptyCompany_ReturnsNoContentAndRemovesIt()
    {
        var company = await _api.CreateCompanyAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{company.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        var (_, items) = await ListAsync(Q(company.Name));
        Assert.Empty(items);
        using var anonymous = _api.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound, (await anonymous.GetAsync($"/api/companies/{company.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_CompanyWithJobOffers_ReturnsBadRequestAndKeepsCompany()
    {
        var company = await _api.CreateCompanyAsync();
        var job = await _api.CreateJobWithStatusAsync(JobOfferStatus.Draft, customize: j => j.CompanyId = company.Id);
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{company.Id}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Company has job offers or members", await response.ReadMessageAsync());
        Assert.Single((await ListAsync(Q(company.Name))).Items);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/jobs/{job.Id}")).StatusCode);
    }

    [Fact]
    public async Task Delete_CompanyWithMembers_ReturnsBadRequestAndKeepsCompanyAndEmployer()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{employer.CompanyId}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Company has job offers or members", await response.ReadMessageAsync());
        using var client = _api.CreateClient(employer);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/employer/company")).StatusCode);
    }

    [Fact]
    public async Task Delete_AfterJobOfferIsRemoved_Succeeds()
    {
        var company = await _api.CreateCompanyAsync();
        var job = await _api.CreateJobAsync(j => j.CompanyId = company.Id);
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await admin.DeleteAsync($"{Url}/{company.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/jobs/{job.Id}")).StatusCode);

        var response = await admin.DeleteAsync($"{Url}/{company.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Fact]
    public async Task Delete_OfMissingCompany_ReturnsNotFound()
    {
        using var admin = await _api.AdminClientAsync();

        var response = await admin.DeleteAsync($"{Url}/{int.MaxValue}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("Company not found", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Delete_ThenCreateWithSameName_Succeeds()
    {
        var company = await _api.CreateCompanyAsync();
        using var admin = await _api.AdminClientAsync();
        Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"{Url}/{company.Id}")).StatusCode);

        var recreated = await _api.CreateCompanyAsync(company.Name);

        Assert.NotEqual(company.Id, recreated.Id);
    }
}
