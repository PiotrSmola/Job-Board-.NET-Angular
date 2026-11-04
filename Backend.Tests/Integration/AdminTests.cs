using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Endpointy admina na współdzielonej bazie (środowisko Development). Wywołanie refresh-seed w trybie
// włączonym usuwa wszystkich użytkowników, więc jest testowane tylko na osobnych fabrykach (AdminSeedRefreshTests).
[Collection(ApiCollection.Name)]
public class AdminTests
{
    private readonly ApiFixture _api;

    public AdminTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Settings_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task Settings_AsRegularUser_ReturnsForbidden()
    {
        using var client = await _api.UserClientAsync();

        Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync("/api/admin/settings")).StatusCode);
    }

    [Fact]
    public async Task Settings_InDevelopment_AllowsSeedRefreshEvenWhenConfigFlagIsFalse()
    {
        // fixture ma Admin:AllowSeedRefresh=false, ale środowisko to Development
        using var client = await _api.AdminClientAsync();

        var response = await client.GetAsync("/api/admin/settings");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(body.RootElement.GetProperty("allowSeedRefresh").GetBoolean());
    }

    [Fact]
    public async Task RefreshSeed_WithoutToken_ReturnsUnauthorized()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsync("/api/admin/refresh-seed", null)).StatusCode);
    }

    [Fact]
    public async Task RefreshSeed_AsRegularUser_ReturnsForbiddenAndLeavesDataIntact()
    {
        var user = await _api.RegisterUserAsync("norefresh");
        using var client = _api.CreateClient(user);

        var response = await client.PostAsync("/api/admin/refresh-seed", null);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        // użytkownik nadal istnieje i może się zalogować
        Assert.False(string.IsNullOrEmpty(await _api.LoginAsync(user.Email, user.Password)));
    }
}
