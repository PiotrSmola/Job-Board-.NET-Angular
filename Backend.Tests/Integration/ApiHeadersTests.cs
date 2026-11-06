using System.Net;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Backend.Tests.Integration;

// Nagłówki odpowiedzi API (SecurityHeadersMiddleware, Kestrel): nosniff zawsze, no-store dla odpowiedzi z danymi
// użytkownika (żądanie z tokenem albo endpoint wymagający zalogowania), bez nagłówka Server
[Collection(ApiCollection.Name)]
public class ApiHeadersTests
{
    private readonly ApiFixture _api;

    public ApiHeadersTests(ApiFixture api) => _api = api;

    private static void AssertNoSniff(HttpResponseMessage response) =>
        Assert.Equal("nosniff", Assert.Single(response.Headers.GetValues("X-Content-Type-Options")));

    private static void AssertNoStore(HttpResponseMessage response) =>
        Assert.True(response.Headers.CacheControl?.NoStore, $"Cache-Control: no-store expected, got '{response.Headers.CacheControl}'");

    [Fact]
    public async Task PublicListWithoutToken_IsNotMarkedNoStore()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobs");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoSniff(response);
        Assert.Null(response.Headers.CacheControl);
    }

    [Theory]
    [InlineData("/api/jobapplications/my-applications")]
    [InlineData("/api/jobs")]
    [InlineData("/api/companies")]
    public async Task AuthenticatedResponses_AreNotStored(string url)
    {
        using var client = await _api.UserClientAsync();

        var response = await client.GetAsync(url);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        AssertNoSniff(response);
        AssertNoStore(response);
    }

    [Fact]
    public async Task ProtectedEndpointWithoutToken_401IsNotStoredEither()
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync("/api/jobapplications/my-applications");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        AssertNoSniff(response);
        AssertNoStore(response);
    }

    [Fact]
    public async Task ErrorResponses_GetTheHeadersToo()
    {
        using var client = _api.CreateClient();

        var notFound = await client.GetAsync("/api/jobs/999999999");
        var invalid = await client.PostJsonAsync("/api/auth/login", new { });

        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        AssertNoSniff(notFound);
        AssertNoSniff(invalid);
    }

    [Fact]
    public void Kestrel_DoesNotSendTheServerHeader()
    {
        var options = _api.Factory.Services.GetRequiredService<IOptions<KestrelServerOptions>>().Value;

        Assert.False(options.AddServerHeader);
    }
}
