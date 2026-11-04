using System.Net;
using System.Security.Claims;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

// Walidacja tokenów JWT przez middleware uwierzytelniania (podpis, wystawca, odbiorca, ważność, role,
// istnienie użytkownika i security stamp)
[Collection(ApiCollection.Name)]
public class JwtAuthenticationTests
{
    private const string ProtectedUrl = "/api/jobapplications/my-applications";
    private const string AdminUrl = "/api/admin/settings";

    private readonly ApiFixture _api;

    public JwtAuthenticationTests(ApiFixture api) => _api = api;

    private async Task<HttpStatusCode> GetStatusAsync(string url, string? token)
    {
        using var client = _api.CreateClient(token);
        return (await client.GetAsync(url)).StatusCode;
    }

    // Id i security stamp istniejącego użytkownika - tokeny w testach negatywnych różnią się tylko badaną cechą
    private async Task<(string UserId, string Stamp)> ValidIdentityAsync()
    {
        var user = await _api.RegisterUserAsync("jwt");
        var stamp = JwtTestHelper.GetSecurityStamp(user.Token);
        Assert.False(string.IsNullOrEmpty(stamp));
        return (user.Id, stamp!);
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutToken_ReturnsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, null));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenFromLogin_ReturnsOk()
    {
        var token = await _api.LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword);

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithMalformedToken_ReturnsUnauthorized()
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, "not.a.jwt"));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTamperedSignature_ReturnsUnauthorized()
    {
        var token = await _api.LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword);
        var tampered = token[..^3] + (token.EndsWith("AAA") ? "BBB" : "AAA");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, tampered));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithTokenSignedByDifferentSecret_ReturnsUnauthorized()
    {
        var (userId, stamp) = await ValidIdentityAsync();
        var forged = JwtTestHelper.Create("a-completely-different-signing-secret-0123456789", userId: userId, securityStamp: stamp);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, forged));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongIssuer_ReturnsUnauthorized()
    {
        var (userId, stamp) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, issuer: "SomeoneElse", userId: userId, securityStamp: stamp);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongAudience_ReturnsUnauthorized()
    {
        var (userId, stamp) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, audience: "SomeoneElse", userId: userId, securityStamp: stamp);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_ReturnsUnauthorized()
    {
        // poza domyślnym ClockSkew (5 min)
        var (userId, stamp) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, expires: DateTime.UtcNow.AddHours(-1), userId: userId, securityStamp: stamp);

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithCorrectlySignedTokenOfExistingUser_ReturnsOk()
    {
        // kontrola dodatnia dla testów powyżej - podpis kluczem testowym + istniejący użytkownik z aktualnym stampem
        var (userId, stamp) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, userId: userId, role: "User", securityStamp: stamp);

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithCorrectlySignedTokenOfUnknownUser_ReturnsUnauthorized()
    {
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, userId: "no-such-user", role: "User", securityStamp: "stamp");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongSecurityStamp_ReturnsUnauthorized()
    {
        var (userId, _) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, userId: userId, role: "User", securityStamp: "outdated-stamp");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithoutSecurityStampClaim_ReturnsUnauthorized()
    {
        var (userId, _) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, userId: userId, role: "User");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_AfterPasswordChange_RejectsPreviouslyIssuedToken()
    {
        var user = await _api.RegisterUserAsync("pwchange");
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(ProtectedUrl, user.Token));

        // zmiana hasła odświeża security stamp
        using (var scope = _api.Factory.Services.CreateScope())
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var entity = await userManager.FindByIdAsync(user.Id);
            var result = await userManager.ChangePasswordAsync(entity!, user.Password, "N3wPassword");
            Assert.True(result.Succeeded);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, user.Token));
        var newToken = await _api.LoginAsync(user.Email, "N3wPassword");
        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(ProtectedUrl, newToken));
    }

    [Fact]
    public async Task AdminEndpoint_WithUserRoleToken_ReturnsForbidden()
    {
        var token = await _api.LoginAsync(TestUsers.UserEmail, TestUsers.UserPassword);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(AdminUrl, token));
    }

    [Fact]
    public async Task AdminEndpoint_WithAdminRoleToken_ReturnsOk()
    {
        var token = await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword);

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(AdminUrl, token));
    }

    [Fact]
    public async Task AdminEndpoint_WithTokenWithoutRoles_ReturnsForbidden()
    {
        var (userId, stamp) = await ValidIdentityAsync();
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, role: null, userId: userId, securityStamp: stamp);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(AdminUrl, token));
    }

    [Fact]
    public async Task IssuedToken_ContainsIdentityClaimsSecurityStampAndDayLongExpiry()
    {
        var token = await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword);

        var jwt = JwtTestHelper.Read(token);

        Assert.Equal("JobBoardAPI", jwt.Issuer);
        Assert.Contains("JobBoardClient", jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Email || c.Type == "email");
        Assert.Contains(jwt.Claims, c => (c.Type == ClaimTypes.Email || c.Type == "email") && c.Value == TestUsers.AdminEmail);
        Assert.False(string.IsNullOrEmpty(JwtTestHelper.GetUserId(token)));
        Assert.False(string.IsNullOrEmpty(JwtTestHelper.GetSecurityStamp(token)));
        var lifetime = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, 1430, 1441);
    }
}
