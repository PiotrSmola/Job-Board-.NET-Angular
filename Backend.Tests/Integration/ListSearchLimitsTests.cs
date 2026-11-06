using System.Net;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Tekst wyszukiwania i filtr lokalizacji list ograniczone do 100 znaków: dłuższy to 400 z błędem walidacji pola,
// zanim trafi do zapytania LIKE
[Collection(ApiCollection.Name)]
public class ListSearchLimitsTests
{
    private readonly ApiFixture _api;

    public ListSearchLimitsTests(ApiFixture api) => _api = api;

    public static TheoryData<string, string, string> Filters => new()
    {
        { "anonymous", "/api/jobs", "SearchTerm" },
        { "anonymous", "/api/jobs", "Location" },
        { "anonymous", "/api/companies", "SearchTerm" },
        { "admin", "/api/jobs/manage", "SearchTerm" },
        { "admin", "/api/jobs/manage", "Location" },
        { "admin", "/api/jobapplications/all", "SearchTerm" },
        { "admin", "/api/admin/companies", "SearchTerm" }
    };

    private async Task<HttpClient> ClientAsync(string who) =>
        who == "admin" ? await _api.AdminClientAsync() : _api.CreateClient();

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task TooLongFilter_ReturnsBadRequest(string who, string url, string field)
    {
        using var client = await ClientAsync(who);

        var response = await client.GetAsync($"{url}?{field}={new string('a', PaginationParams.MaxSearchLength + 1)}");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(field, await response.ReadValidationFieldsAsync());
    }

    [Theory]
    [MemberData(nameof(Filters))]
    public async Task FilterAtTheLimit_IsAccepted(string who, string url, string field)
    {
        using var client = await ClientAsync(who);

        var response = await client.GetAsync($"{url}?{field}={new string('a', PaginationParams.MaxSearchLength)}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(100, PaginationParams.MaxSearchLength);
    }
}
