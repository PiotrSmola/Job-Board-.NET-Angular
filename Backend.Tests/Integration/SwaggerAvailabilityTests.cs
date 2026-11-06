using System.Net;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Swagger: zawsze w Development, poza nim tylko po jawnym Swagger:Enabled=true (lokalny stack Dockera - SWAGGER_ENABLED)
[Collection(ApiCollection.Name)]
public class SwaggerInDevelopmentTests
{
    private readonly ApiFixture _api;

    public SwaggerInDevelopmentTests(ApiFixture api) => _api = api;

    [Fact]
    public async Task Development_ServesSwagger()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }
}

public class SwaggerInProductionTests : IClassFixture<ProductionRefreshDisabledFixture>
{
    private readonly ProductionRefreshDisabledFixture _api;

    public SwaggerInProductionTests(ProductionRefreshDisabledFixture api) => _api = api;

    [Fact]
    public async Task ProductionWithoutTheSetting_DoesNotServeSwagger()
    {
        using var client = _api.CreateClient();

        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/swagger/index.html")).StatusCode);
    }

    [Theory]
    [InlineData("true", HttpStatusCode.OK)]
    [InlineData("false", HttpStatusCode.NotFound)]
    public async Task ProductionWithTheSetting_FollowsIt(string enabled, HttpStatusCode expected)
    {
        var factory = new JobBoardFactory("Production").WithSetting("Swagger:Enabled", enabled);
        try
        {
            using var client = factory.CreateClient();

            Assert.Equal(expected, (await client.GetAsync("/swagger/v1/swagger.json")).StatusCode);
        }
        finally
        {
            await factory.DisposeAndDropDatabaseAsync();
        }
    }
}
