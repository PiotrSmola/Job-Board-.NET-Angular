using System.Net;
using System.Text.Json;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Numer strony daleko za końcem listy (aż do int.MaxValue) to pusta strona 200 - wcześniej przesunięcie
// (pageNumber - 1) * pageSize przepełniało int i zapytanie z ujemnym OFFSET kończyło się 500
[Collection(ApiCollection.Name)]
public class PaginationOverflowTests
{
    private readonly ApiFixture _api;

    public PaginationOverflowTests(ApiFixture api) => _api = api;

    public static TheoryData<string, string> Lists => new()
    {
        { "anonymous", "/api/jobs" },
        { "anonymous", "/api/companies" },
        { "admin", "/api/jobs/manage" },
        { "admin", "/api/jobapplications/all" },
        { "admin", "/api/admin/companies" }
    };

    private async Task<HttpClient> ClientAsync(string who) =>
        who == "admin" ? await _api.AdminClientAsync() : _api.CreateClient();

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task HugePageNumber_ReturnsAnEmptyPageWithTheLastAllowedPageNumber(string who, string url)
    {
        using var client = await ClientAsync(who);

        foreach (var pageSize in new[] { 10, PaginationParams.MaxPageSize })
        {
            var response = await client.GetAsync($"{url}?pageNumber={int.MaxValue}&pageSize={pageSize}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(0, body.RootElement.GetArrayLength());
            var pagination = response.ReadPagination();
            Assert.Equal(PaginationParams.MaxPageNumber, pagination.CurrentPage);
            Assert.Equal(pageSize, pagination.ItemsPerPage);
        }
    }

    [Theory]
    [MemberData(nameof(Lists))]
    public async Task PageJustPastTheEnd_IsEmptyAndReportsTheTotals(string who, string url)
    {
        using var client = await ClientAsync(who);
        var first = await client.GetAsync($"{url}?pageSize=1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        var totals = first.ReadPagination();

        var past = await client.GetAsync($"{url}?pageSize=1&pageNumber={totals.TotalPages + 1}");

        Assert.Equal(HttpStatusCode.OK, past.StatusCode);
        using var body = JsonDocument.Parse(await past.Content.ReadAsStringAsync());
        Assert.Equal(0, body.RootElement.GetArrayLength());
        Assert.Equal(totals.TotalPages + 1, past.ReadPagination().CurrentPage);
    }

    [Theory]
    [InlineData("-5")]
    [InlineData("0")]
    public async Task NonPositivePageNumber_ReturnsTheFirstPage(string pageNumber)
    {
        using var client = _api.CreateClient();

        var response = await client.GetAsync($"/api/jobs?pageNumber={pageNumber}");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(1, response.ReadPagination().CurrentPage);
    }
}
