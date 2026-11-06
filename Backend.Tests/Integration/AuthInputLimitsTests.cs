using System.Net;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Limity długości e-maila (256 - kolumna Identity) i hasła (128 - koszt haszowania) w każdym DTO z danymi logowania:
// za długa wartość to 400 z błędem walidacji pola, zanim dojdzie do bazy albo haszowania (wcześniej e-mail > 256 dawał 500)
[Collection(ApiCollection.Name)]
public class AuthInputLimitsTests
{
    private readonly ApiFixture _api;

    public AuthInputLimitsTests(ApiFixture api) => _api = api;

    // poprawny format adresu o zadanej długości
    private static string EmailOfLength(int length)
    {
        const string domain = "@test.local";
        var local = Guid.NewGuid().ToString("N");
        return local.PadRight(length - domain.Length, 'a') + domain;
    }

    private static RegisterDto NewRegisterDto(string? email = null, string password = "Passw0rd1") => new()
    {
        Email = email ?? EmailOfLength(40),
        Password = password,
        FirstName = "Anna",
        LastName = "Testowa"
    };

    private static RegisterEmployerDto NewEmployerDto(string? email = null, string password = "Passw0rd1") => new()
    {
        Email = email ?? EmailOfLength(40),
        Password = password,
        FirstName = "Emp",
        LastName = "Loyer",
        CompanyName = ApiFixture.UniqueName("Firma")
    };

    private async Task AssertFieldRejectedAsync(string url, object dto, string field)
    {
        using var client = _api.CreateClient();

        var response = await client.PostJsonAsync(url, dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public void Limits_Are256CharactersForEmailAnd128ForPassword()
    {
        Assert.Equal(256, CredentialLimits.MaxEmailLength);
        Assert.Equal(128, CredentialLimits.MaxPasswordLength);
        Assert.Equal(300, EmailOfLength(300).Length);
    }

    [Theory]
    [InlineData(257)]
    [InlineData(300)]
    public async Task Register_WithTooLongEmail_ReturnsBadRequest(int length)
    {
        await AssertFieldRejectedAsync("/api/auth/register", NewRegisterDto(EmailOfLength(length)), "Email");
    }

    [Fact]
    public async Task RegisterEmployer_WithTooLongEmail_ReturnsBadRequestAndCreatesNoCompany()
    {
        var dto = NewEmployerDto(EmailOfLength(300));

        await AssertFieldRejectedAsync("/api/auth/register-employer", dto, "Email");

        Assert.Equal(0, await _api.QueryDbAsync(db => Task.FromResult(db.Companies.Count(c => c.Name == dto.CompanyName))));
    }

    [Fact]
    public async Task Login_WithTooLongEmail_ReturnsBadRequest()
    {
        await AssertFieldRejectedAsync("/api/auth/login", new LoginDto { Email = EmailOfLength(257), Password = "Passw0rd1" }, "Email");
    }

    [Fact]
    public async Task Register_WithTooLongPassword_ReturnsBadRequest()
    {
        await AssertFieldRejectedAsync("/api/auth/register", NewRegisterDto(password: "Aa1" + new string('x', 126)), "Password");
        await AssertFieldRejectedAsync("/api/auth/register-employer", NewEmployerDto(password: "Aa1" + new string('x', 126)), "Password");
    }

    [Fact]
    public async Task Login_WithTooLongPassword_ReturnsBadRequest()
    {
        await AssertFieldRejectedAsync("/api/auth/login",
            new LoginDto { Email = TestUsers.UserEmail, Password = new string('x', 129) }, "Password");
    }

    [Fact]
    public async Task EmailAndPasswordAtTheLimits_AreAccepted()
    {
        var dto = NewRegisterDto(EmailOfLength(256), "Aa1" + new string('x', 125));
        using var client = _api.CreateClient();

        var registered = await client.PostJsonAsync("/api/auth/register", dto);
        var loggedIn = await client.PostJsonAsync("/api/auth/login", new LoginDto { Email = dto.Email, Password = dto.Password });

        Assert.Equal(HttpStatusCode.OK, registered.StatusCode);
        Assert.Equal(HttpStatusCode.OK, loggedIn.StatusCode);
    }
}
