using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Profil, zmiana hasła i preferencje powiadomień zalogowanego użytkownika (/api/account)
[Collection(ApiCollection.Name)]
public class AccountProfileTests
{
    private readonly ApiFixture _api;

    public AccountProfileTests(ApiFixture api) => _api = api;

    private static UpdateProfileDto ValidProfile(Action<UpdateProfileDto>? customize = null)
    {
        var dto = new UpdateProfileDto
        {
            FirstName = "Anna",
            LastName = "Nowak",
            PhoneNumber = "+48 123-456-789",
            Headline = "Frontend Developer",
            Location = "Rzeszów",
            LinkedInUrl = "https://www.linkedin.com/in/anna-nowak",
            About = "Lubię Angulara."
        };
        customize?.Invoke(dto);
        return dto;
    }

    [Fact]
    public async Task GetProfile_ReturnsTheAccountData()
    {
        var user = await _api.RegisterUserAsync("profile");
        using var client = _api.CreateClient(user);

        var response = await client.GetAsync(AccountApi.ProfileUrl);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.ReadJsonAsync<ProfileDto>();
        Assert.Equal(user.Email, profile.Email);
        Assert.Equal("Test", profile.FirstName);
        Assert.Equal("User", profile.LastName);
        Assert.Null(profile.PhoneNumber);
        Assert.Null(profile.LinkedInUrl);
        Assert.False(profile.EmailConfirmed);
        Assert.Equal([Roles.User], profile.Roles);
        Assert.True(profile.CreatedAt > DateTime.UtcNow.AddMinutes(-5));
    }

    [Fact]
    public async Task GetProfile_HasExactlyTheContractFields()
    {
        var user = await _api.RegisterUserAsync("profile");
        using var client = _api.CreateClient(user);

        using var body = JsonDocument.Parse(await (await client.GetAsync(AccountApi.ProfileUrl)).Content.ReadAsStringAsync());

        Assert.Equal(
            new[] { "about", "createdAt", "email", "emailConfirmed", "firstName", "headline", "lastName", "linkedInUrl", "location", "phoneNumber", "roles" },
            body.RootElement.EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task UpdateProfile_TrimsValuesStoresEmptyOptionalFieldsAsNullAndKeepsTheEmail()
    {
        var user = await _api.RegisterUserAsync("profile");
        using var client = _api.CreateClient(user);

        var response = await client.PutJsonAsync(AccountApi.ProfileUrl, ValidProfile(p =>
        {
            p.FirstName = "  Anna ";
            p.Headline = "   ";
            p.About = "";
            p.Location = " Rzeszów ";
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var profile = await response.ReadJsonAsync<ProfileDto>();
        Assert.Equal("Anna", profile.FirstName);
        Assert.Null(profile.Headline);
        Assert.Null(profile.About);
        Assert.Equal("Rzeszów", profile.Location);
        Assert.Equal("+48 123-456-789", profile.PhoneNumber);
        Assert.Equal(user.Email, profile.Email);

        var reloaded = await (await client.GetAsync(AccountApi.ProfileUrl)).ReadJsonAsync<ProfileDto>();
        Assert.Equivalent(profile, reloaded);

        // profil nie zmienia security stampu - token nadal ważny (sprawdzone wyżej GET-em)
    }

    [Theory]
    [InlineData("https://linkedin.com/in/x")]
    [InlineData("HTTPS://WWW.LINKEDIN.COM/in/x")]
    [InlineData("https://www.linkedin.com/company/acme?trk=1")]
    public void LinkedInUrl_AcceptedFormats_PassValidation(string url)
    {
        Assert.True(IsValid(ValidProfile(p => p.LinkedInUrl = url)));
    }

    [Theory]
    [InlineData(nameof(UpdateProfileDto.FirstName), null)]
    [InlineData(nameof(UpdateProfileDto.FirstName), "   ")]
    [InlineData(nameof(UpdateProfileDto.LastName), "")]
    [InlineData(nameof(UpdateProfileDto.FirstName), "x101")]
    [InlineData(nameof(UpdateProfileDto.PhoneNumber), "123abc")]
    [InlineData(nameof(UpdateProfileDto.PhoneNumber), "123\n")]
    [InlineData(nameof(UpdateProfileDto.PhoneNumber), "x31")]
    [InlineData(nameof(UpdateProfileDto.Headline), "x151")]
    [InlineData(nameof(UpdateProfileDto.Location), "x201")]
    [InlineData(nameof(UpdateProfileDto.About), "x2001")]
    [InlineData(nameof(UpdateProfileDto.LinkedInUrl), "http://www.linkedin.com/in/x")]
    [InlineData(nameof(UpdateProfileDto.LinkedInUrl), "https://www.linkedin.com/")]
    [InlineData(nameof(UpdateProfileDto.LinkedInUrl), "https://linkedin.com.evil.test/in/x")]
    [InlineData(nameof(UpdateProfileDto.LinkedInUrl), "https://evil.test/https://linkedin.com/in/x")]
    [InlineData(nameof(UpdateProfileDto.LinkedInUrl), "https://www.linkedin.com/in/x y")]
    public async Task UpdateProfile_WithInvalidField_Returns400ValidationProblem(string field, string? value)
    {
        var user = await _api.RegisterUserAsync("profile");
        using var client = _api.CreateClient(user);
        // "xN" - napis o długości N
        var actual = value is { Length: > 1 } && value[0] == 'x' && int.TryParse(value[1..], out var length)
            ? new string('a', length)
            : value;
        var dto = ValidProfile();
        typeof(UpdateProfileDto).GetProperty(field)!.SetValue(dto, actual);

        var response = await client.PutJsonAsync(AccountApi.ProfileUrl, dto);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.ReadValidationFieldsAsync());
    }

    [Fact]
    public async Task Profile_RequiresAuthentication()
    {
        using var anonymous = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AccountApi.ProfileUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PutJsonAsync(AccountApi.ProfileUrl, ValidProfile())).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AccountApi.NotificationsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.ChangePasswordAsync("a", "b")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.DeleteAccountAsync("a")).StatusCode);
    }

    [Fact]
    public async Task Profile_WorksForEmployersAndAdmins()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var employerClient = _api.CreateClient(employer);
        var employerProfile = await (await employerClient.GetAsync(AccountApi.ProfileUrl)).ReadJsonAsync<ProfileDto>();
        Assert.Equal([Roles.Employer], employerProfile.Roles);

        using var admin = await _api.AdminClientAsync();
        var adminProfile = await (await admin.GetAsync(AccountApi.ProfileUrl)).ReadJsonAsync<ProfileDto>();
        Assert.Contains(Roles.Admin, adminProfile.Roles);
        Assert.True(adminProfile.EmailConfirmed);
    }

    [Fact]
    public async Task ChangePassword_ReturnsANewTokenAndRevokesTheOldOne()
    {
        var user = await _api.RegisterUserAsync("password");
        using var oldClient = _api.CreateClient(user);

        var response = await oldClient.ChangePasswordAsync(user.Password, "Changed-Passw0rd");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.ReadJsonAsync<UserDto>();
        Assert.Equal(user.Email, dto.Email);
        Assert.False(dto.EmailConfirmed);
        Assert.NotEqual(user.Token, dto.Token);

        // stary token - 401 (nowy security stamp), nowy działa
        Assert.Equal(HttpStatusCode.Unauthorized, (await oldClient.GetAsync(AccountApi.ProfileUrl)).StatusCode);
        using var newClient = _api.CreateClient(dto.Token);
        Assert.Equal(HttpStatusCode.OK, (await newClient.GetAsync(AccountApi.ProfileUrl)).StatusCode);
        Assert.NotEmpty(await _api.LoginAsync(user.Email, "Changed-Passw0rd"));
    }

    [Fact]
    public async Task ChangePassword_WithAWrongCurrentPassword_Returns400AndKeepsThePassword()
    {
        var user = await _api.RegisterUserAsync("password");
        using var client = _api.CreateClient(user);

        var response = await client.ChangePasswordAsync("Wrong-Passw0rd", "Changed-Passw0rd");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Current password is incorrect", await response.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithAWeakNewPassword_ReturnsIdentityErrorsUnderNewPassword()
    {
        var user = await _api.RegisterUserAsync("password");
        using var client = _api.CreateClient(user);

        var response = await client.ChangePasswordAsync(user.Password, "short");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors").GetProperty("NewPassword").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.Contains(errors, e => e.StartsWith("Passwords must be at least 6 characters"));
        // token nadal ważny - nic się nie zmieniło
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(AccountApi.ProfileUrl)).StatusCode);
    }

    [Fact]
    public async Task ChangePassword_WithPasswordsLongerThan100Characters_Returns400()
    {
        var user = await _api.RegisterUserAsync("password");
        using var client = _api.CreateClient(user);
        var tooLong = "Aa1" + new string('x', 98);

        var response = await client.ChangePasswordAsync(tooLong, tooLong);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var fields = await response.ReadValidationFieldsAsync();
        Assert.Contains("CurrentPassword", fields);
        Assert.Contains("NewPassword", fields);
    }

    [Fact]
    public async Task ChangePassword_WrongAttemptsLockTheAccountLikeLogin()
    {
        var user = await _api.RegisterUserAsync("password");
        using var client = _api.CreateClient(user);
        for (var i = 0; i < 5; i++)
        {
            await client.ChangePasswordAsync("Wrong-Passw0rd", "Changed-Passw0rd");
        }

        var response = await client.ChangePasswordAsync(user.Password, "Changed-Passw0rd");

        Assert.Equal(HttpStatusCode.Locked, response.StatusCode);
        Assert.Equal("Account is temporarily locked due to too many failed login attempts", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task NotificationPreferences_DefaultToTrueAndCanBeChanged()
    {
        var user = await _api.RegisterUserAsync("notify");
        using var client = _api.CreateClient(user);

        var defaults = await (await client.GetAsync(AccountApi.NotificationsUrl)).ReadJsonAsync<NotificationPreferencesDto>();
        Assert.True(defaults.ApplicationStatusChanged);
        Assert.True(defaults.NewApplication);
        Assert.True(defaults.CompanyReview);

        var update = new NotificationPreferencesDto { ApplicationStatusChanged = false, NewApplication = true, CompanyReview = false };
        var response = await client.PutJsonAsync(AccountApi.NotificationsUrl, update);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equivalent(update, await response.ReadJsonAsync<NotificationPreferencesDto>());
        Assert.Equivalent(update, await (await client.GetAsync(AccountApi.NotificationsUrl)).ReadJsonAsync<NotificationPreferencesDto>());
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"applicationStatusChanged\":true,\"newApplication\":true}")]
    [InlineData("{\"applicationStatusChanged\":true,\"newApplication\":true,\"companyReview\":null}")]
    [InlineData("{\"applicationStatusChanged\":\"yes\",\"newApplication\":true,\"companyReview\":true}")]
    public async Task NotificationPreferences_RequireAllThreeBooleans(string json)
    {
        var user = await _api.RegisterUserAsync("notify");
        using var client = _api.CreateClient(user);

        var response = await client.PutRawJsonAsync(AccountApi.NotificationsUrl, json);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private static bool IsValid(object dto)
    {
        var context = new System.ComponentModel.DataAnnotations.ValidationContext(dto);
        return System.ComponentModel.DataAnnotations.Validator.TryValidateObject(dto, context, null, validateAllProperties: true);
    }
}
