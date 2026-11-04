using Backend.Data;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

// Sanity check infrastruktury: testy muszą trafiać do własnej bazy, a nie do JobBoardDb
[Collection(ApiCollection.Name)]
public class TestEnvironmentTests
{
    private readonly ApiFixture _api;

    public TestEnvironmentTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Application_UsesDedicatedTestDatabase_NotConfiguredDefaultDatabase()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        var connection = context.Database.GetDbConnection();

        Assert.Equal(_api.Factory.DatabaseName, connection.Database);
        Assert.StartsWith(TestDatabase.NamePrefix, connection.Database);
        Assert.NotEqual("JobBoardDb", connection.Database);
        Assert.True(await context.Database.CanConnectAsync());
    }

    [Fact]
    public async Task Database_ContainsSeededData()
    {
        using var scope = _api.Factory.Services.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

        Assert.True(await context.JobOffers.CountAsync() >= 20);
        Assert.True(await context.Users.CountAsync() >= 7);
    }
}
