using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// POST /api/auth/register-employer: konto z rolą Employer + firma oczekująca na weryfikację w jednej transakcji
[Collection(ApiCollection.Name)]
public class EmployerRegistrationTests
{
    private const string Url = "/api/auth/register-employer";
    private const string NameTakenMessage = "Company name is already taken";

    private readonly ApiFixture _api;

    public EmployerRegistrationTests(ApiFixture api) => _api = api;

    private static RegisterEmployerDto NewDto(string? companyName = null, Action<RegisterEmployerDto>? customize = null)
    {
        var dto = new RegisterEmployerDto
        {
            Email = $"reg_{Guid.NewGuid():N}@test.local",
            Password = "Passw0rd1",
            FirstName = "Anna",
            LastName = "Nowak",
            CompanyName = companyName ?? ApiFixture.UniqueName("Rejestracja")
        };
        customize?.Invoke(dto);
        return dto;
    }

    private async Task<HttpResponseMessage> RegisterAsync(RegisterEmployerDto dto)
    {
        using var client = _api.CreateClient();
        return await client.PostJsonAsync(Url, dto);
    }

    private static async Task<string?> MessageAsync(HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString();
    }

    // firmy o danej nazwie widziane przez admina (wszystkie statusy)
    private async Task<CompanyDto[]> AdminFindCompaniesAsync(string searchTerm)
    {
        using var admin = await _api.AdminClientAsync();
        var response = await admin.GetAsync($"/api/admin/companies?searchTerm={Uri.EscapeDataString(searchTerm)}&pageSize=50");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<CompanyDto[]>();
    }

    private async Task<HttpResponseMessage> TryLoginAsync(string email, string password)
    {
        using var client = _api.CreateClient();
        return await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = email, Password = password });
    }

    // --- sukces ---

    [Fact]
    public async Task RegisterEmployer_WithValidData_ReturnsUserWithEmployerRoleToken()
    {
        var dto = NewDto();

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.Equal(dto.Email, user.Email);
        Assert.Equal("Anna", user.FirstName);
        Assert.Equal("Nowak", user.LastName);
        Assert.Equal(new[] { Roles.Employer }, JwtTestHelper.GetRoles(user.Token));
    }

    [Fact]
    public async Task RegisterEmployer_CreatesPendingCompanyWithGivenData()
    {
        var dto = NewDto(customize: d =>
        {
            d.CompanyDescription = "Opis firmy";
            d.Website = "https://example.com/about";
            d.Location = "Rzeszów";
        });

        var user = await (await RegisterAsync(dto)).ReadJsonAsync<UserDto>();

        using var client = _api.CreateClient(user.Token);
        var response = await client.GetAsync("/api/employer/company");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var company = await response.ReadJsonAsync<CompanyDto>();
        Assert.Equal(dto.CompanyName, company.Name);
        Assert.Equal("Opis firmy", company.Description);
        Assert.Equal("https://example.com/about", company.Website);
        Assert.Equal("Rzeszów", company.Location);
        Assert.Equal(CompanyStatus.Pending, company.Status);
        Assert.Null(company.RejectionReason);
        Assert.Null(company.ReviewedAt);
        Assert.Equal(0, company.ActiveJobsCount);
        Assert.True(Math.Abs((DateTime.UtcNow - company.CreatedAt).TotalMinutes) < 5);
    }

    [Fact]
    public async Task RegisterEmployer_WithoutOptionalFields_StoresNulls()
    {
        var dto = NewDto(customize: d =>
        {
            d.CompanyDescription = "   ";
            d.Website = "";
            d.Location = null;
        });

        var user = await (await RegisterAsync(dto)).ReadJsonAsync<UserDto>();

        using var client = _api.CreateClient(user.Token);
        var company = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Null(company.Description);
        Assert.Null(company.Website);
        Assert.Null(company.Location);
    }

    [Fact]
    public async Task RegisterEmployer_NormalizesCompanyNameWhitespace()
    {
        var unique = ApiFixture.UniqueName("Spacje");
        var padded = $"   {unique.Replace(" ", "   ")}  ";

        var user = await (await RegisterAsync(NewDto(padded))).ReadJsonAsync<UserDto>();

        using var client = _api.CreateClient(user.Token);
        var company = await (await client.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(unique, company.Name);
    }

    [Fact]
    public async Task RegisterEmployer_CanLogInAfterwardsWithEmployerRole()
    {
        var dto = NewDto();
        Assert.Equal(HttpStatusCode.OK, (await RegisterAsync(dto)).StatusCode);

        var login = await TryLoginAsync(dto.Email, dto.Password);

        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var user = await login.ReadJsonAsync<UserDto>();
        Assert.Equal(new[] { Roles.Employer }, JwtTestHelper.GetRoles(user.Token));
    }

    [Fact]
    public async Task RegisterEmployer_IgnoresRoleSuppliedInBody()
    {
        var json = $$"""
            {"email":"escalate_{{Guid.NewGuid():N}}@test.local","password":"Passw0rd1","firstName":"A","lastName":"B",
             "companyName":"{{ApiFixture.UniqueName("Eskalacja")}}","role":"Admin","roles":["Admin"],"companyId":1}
            """;
        using var client = _api.CreateClient();

        var response = await client.PostRawJsonAsync(Url, json);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var user = await response.ReadJsonAsync<UserDto>();
        Assert.Equal(new[] { Roles.Employer }, JwtTestHelper.GetRoles(user.Token));
    }

    [Fact]
    public async Task RegisterEmployer_NewCompanyDoesNotAppearInPublicListUntilApproved()
    {
        var dto = NewDto();
        await RegisterAsync(dto);
        using var anonymous = _api.CreateClient();

        var response = await anonymous.GetAsync($"/api/companies?searchTerm={Uri.EscapeDataString(dto.CompanyName)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.ReadJsonAsync<CompanyDto[]>());
    }

    [Fact]
    public async Task RegisterUser_StillAssignsOnlyUserRole()
    {
        var user = await _api.RegisterUserAsync();

        Assert.Equal(new[] { Roles.User }, JwtTestHelper.GetRoles(user.Token));
    }

    // --- zajęta nazwa firmy ---

    [Fact]
    public async Task RegisterEmployer_WithTakenCompanyName_ReturnsBadRequest()
    {
        var existing = await _api.RegisterEmployerAsync();

        var response = await RegisterAsync(NewDto(existing.CompanyName));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task RegisterEmployer_WithTakenCompanyNameInDifferentCase_ReturnsBadRequest()
    {
        var existing = await _api.RegisterEmployerAsync();

        var upper = await RegisterAsync(NewDto(existing.CompanyName.ToUpperInvariant()));
        var lower = await RegisterAsync(NewDto(existing.CompanyName.ToLowerInvariant()));

        Assert.Equal(HttpStatusCode.BadRequest, upper.StatusCode);
        Assert.Equal(NameTakenMessage, await MessageAsync(upper));
        Assert.Equal(HttpStatusCode.BadRequest, lower.StatusCode);
        Assert.Equal(NameTakenMessage, await MessageAsync(lower));
    }

    [Fact]
    public async Task RegisterEmployer_WithTakenCompanyNameWithExtraWhitespace_ReturnsBadRequest()
    {
        var existing = await _api.RegisterEmployerAsync();
        var withSpaces = "  " + existing.CompanyName.Replace(" ", "    ") + "\t ";

        var response = await RegisterAsync(NewDto(withSpaces));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task RegisterEmployer_WithNameOfCompanyCreatedByAdmin_ReturnsBadRequest()
    {
        var company = await _api.CreateCompanyAsync();

        var response = await RegisterAsync(NewDto(company.Name));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task RegisterEmployer_WithNameOfSeededCompany_ReturnsBadRequest()
    {
        var response = await RegisterAsync(NewDto("techcorp solutions"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(NameTakenMessage, await MessageAsync(response));
    }

    [Fact]
    public async Task RegisterEmployer_WithTakenCompanyName_DoesNotCreateUser()
    {
        var existing = await _api.RegisterEmployerAsync();
        var dto = NewDto(existing.CompanyName);

        await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.Unauthorized, (await TryLoginAsync(dto.Email, dto.Password)).StatusCode);
    }

    // --- zajęty e-mail ---

    [Fact]
    public async Task RegisterEmployer_WithEmailOfExistingUser_ReturnsBadRequestAndCreatesNoCompany()
    {
        var candidate = await _api.RegisterUserAsync();
        var dto = NewDto(customize: d => d.Email = candidate.Email);

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Email is already taken", await MessageAsync(response));
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
    }

    [Fact]
    public async Task RegisterEmployer_WithEmailOfExistingUserInDifferentCase_ReturnsBadRequest()
    {
        var candidate = await _api.RegisterUserAsync();
        var dto = NewDto(customize: d => d.Email = candidate.Email.ToUpperInvariant());

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
    }

    [Fact]
    public async Task RegisterEmployer_WithEmailOfExistingEmployer_ReturnsBadRequestAndKeepsFirstCompany()
    {
        var first = await _api.RegisterEmployerAsync();
        var dto = NewDto(customize: d => d.Email = first.Email);

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
        using var firstClient = _api.CreateClient(first);
        var company = await (await firstClient.GetAsync("/api/employer/company")).ReadJsonAsync<CompanyDto>();
        Assert.Equal(first.CompanyName, company.Name);
    }

    [Fact]
    public async Task RegisterUser_WithEmailOfEmployer_ReturnsBadRequest()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync("/api/auth/register", new RegisterDto
        {
            Email = employer.Email,
            Password = "Passw0rd1",
            FirstName = "A",
            LastName = "B"
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // --- walidacja ---

    [Fact]
    public async Task RegisterEmployer_WithoutCompanyName_ReturnsValidationError()
    {
        var dto = NewDto(customize: d => d.CompanyName = string.Empty);

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("CompanyName", out _));
        Assert.Equal(HttpStatusCode.Unauthorized, (await TryLoginAsync(dto.Email, dto.Password)).StatusCode);
    }

    [Fact]
    public async Task RegisterEmployer_WithWhitespaceOnlyCompanyName_ReturnsBadRequestAndCreatesNothing()
    {
        var dto = NewDto("   ");

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await TryLoginAsync(dto.Email, dto.Password)).StatusCode);
    }

    [Fact]
    public async Task RegisterEmployer_WithTooLongCompanyName_ReturnsValidationError()
    {
        var response = await RegisterAsync(NewDto(new string('x', 201)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("CompanyName", out _));
    }

    [Fact]
    public async Task RegisterEmployer_WithCompanyNameOfMaxLength_Succeeds()
    {
        var name = new string('M', 190) + Guid.NewGuid().ToString("N")[..10];

        var response = await RegisterAsync(NewDto(name));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task RegisterEmployer_WithTooLongDescription_ReturnsValidationError()
    {
        var response = await RegisterAsync(NewDto(customize: d => d.CompanyDescription = new string('d', 5001)));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("CompanyDescription", out _));
    }

    [Theory]
    [InlineData("not a url")]
    [InlineData("example.com")]
    [InlineData("ftp://example.com")]
    [InlineData("javascript:alert(1)")]
    [InlineData("https://exa mple.com")]
    public async Task RegisterEmployer_WithInvalidWebsite_ReturnsValidationError(string website)
    {
        var dto = NewDto(customize: d => d.Website = website);

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("errors").TryGetProperty("Website", out _));
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
    }

    [Theory]
    [InlineData("http://example.com")]
    [InlineData("https://example.com/path?x=1")]
    public async Task RegisterEmployer_WithHttpOrHttpsWebsite_Succeeds(string website)
    {
        var response = await RegisterAsync(NewDto(customize: d => d.Website = website));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Theory]
    [InlineData("not-an-email")]
    [InlineData("")]
    public async Task RegisterEmployer_WithInvalidEmail_ReturnsBadRequest(string email)
    {
        var dto = NewDto(customize: d => d.Email = email);

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
    }

    [Fact]
    public async Task RegisterEmployer_WithShortPassword_ReturnsValidationError()
    {
        var dto = NewDto(customize: d => d.Password = "Ab1");

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
    }

    [Fact]
    public async Task RegisterEmployer_WithoutFirstName_ReturnsValidationError()
    {
        var dto = NewDto(customize: d => d.FirstName = string.Empty);

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
    }

    // --- transakcyjność ---

    // hasło przechodzi walidację DTO, ale łamie politykę Identity (brak cyfry i wielkiej litery) -
    // dopiero UserManager.CreateAsync odrzuca konto, gdy firma jest już zapisana w transakcji
    [Fact]
    public async Task RegisterEmployer_WhenIdentityRejectsPassword_LeavesNoCompanyAndNoUser()
    {
        var dto = NewDto(customize: d => d.Password = "abcdefgh");

        var response = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await AdminFindCompaniesAsync(dto.CompanyName));
        Assert.Equal(HttpStatusCode.Unauthorized, (await TryLoginAsync(dto.Email, dto.Password)).StatusCode);
    }

    [Fact]
    public async Task RegisterEmployer_AfterFailedAttempt_CompanyNameAndEmailAreStillFree()
    {
        var dto = NewDto(customize: d => d.Password = "abcdefgh");
        Assert.Equal(HttpStatusCode.BadRequest, (await RegisterAsync(dto)).StatusCode);

        dto.Password = "Passw0rd1";
        var retry = await RegisterAsync(dto);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var companies = await AdminFindCompaniesAsync(dto.CompanyName);
        var company = Assert.Single(companies);
        Assert.Equal(1, company.MembersCount);
    }

    [Fact]
    public async Task RegisterEmployer_SuccessfulRegistration_CreatesExactlyOneCompanyWithOneMember()
    {
        var dto = NewDto();
        await RegisterAsync(dto);

        var company = Assert.Single(await AdminFindCompaniesAsync(dto.CompanyName));

        Assert.Equal(CompanyStatus.Pending, company.Status);
        Assert.Equal(1, company.MembersCount);
    }

    [Fact]
    public async Task RegisterEmployer_ConcurrentRequestsWithSameCompanyName_CreateOnlyOneCompany()
    {
        var name = ApiFixture.UniqueName("Wyscig");
        var dtos = Enumerable.Range(0, 4).Select(_ => NewDto(name)).ToArray();

        var responses = await Task.WhenAll(dtos.Select(RegisterAsync));

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.All(responses.Where(r => r.StatusCode != HttpStatusCode.OK), r => Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode));
        var company = Assert.Single(await AdminFindCompaniesAsync(name));
        Assert.Equal(1, company.MembersCount);
    }
}
