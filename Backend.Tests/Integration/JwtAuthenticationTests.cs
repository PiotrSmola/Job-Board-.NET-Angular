using System.Net;
using System.Security.Claims;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Walidacja tokenów JWT przez middleware uwierzytelniania (podpis, wystawca, odbiorca, ważność, role)
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
        var forged = JwtTestHelper.Create("a-completely-different-signing-secret-0123456789");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, forged));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongIssuer_ReturnsUnauthorized()
    {
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, issuer: "SomeoneElse");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithWrongAudience_ReturnsUnauthorized()
    {
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, audience: "SomeoneElse");

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithExpiredToken_ReturnsUnauthorized()
    {
        // poza domyślnym ClockSkew (5 min)
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, expires: DateTime.UtcNow.AddHours(-1));

        Assert.Equal(HttpStatusCode.Unauthorized, await GetStatusAsync(ProtectedUrl, token));
    }

    [Fact]
    public async Task ProtectedEndpoint_WithCorrectlySignedToken_ReturnsOk()
    {
        // kontrola dodatnia dla testów powyżej - sam podpis kluczem testowym jest akceptowany
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, userId: "any-user", role: "User");

        Assert.Equal(HttpStatusCode.OK, await GetStatusAsync(ProtectedUrl, token));
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
        var token = JwtTestHelper.Create(JobBoardFactory.TestJwtSecret, role: null);

        Assert.Equal(HttpStatusCode.Forbidden, await GetStatusAsync(AdminUrl, token));
    }

    [Fact]
    public async Task IssuedToken_ContainsIdentityClaimsAndDayLongExpiry()
    {
        var token = await _api.LoginAsync(TestUsers.AdminEmail, TestUsers.AdminPassword);

        var jwt = JwtTestHelper.Read(token);

        Assert.Equal("JobBoardAPI", jwt.Issuer);
        Assert.Contains("JobBoardClient", jwt.Audiences);
        Assert.Contains(jwt.Claims, c => c.Type == ClaimTypes.Email || c.Type == "email");
        Assert.Contains(jwt.Claims, c => (c.Type == ClaimTypes.Email || c.Type == "email") && c.Value == TestUsers.AdminEmail);
        Assert.False(string.IsNullOrEmpty(JwtTestHelper.GetUserId(token)));
        var lifetime = jwt.ValidTo - DateTime.UtcNow;
        Assert.InRange(lifetime.TotalMinutes, 1430, 1441);
    }
}
