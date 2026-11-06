using System.Net;
using Backend.DTOs;

namespace Backend.Tests.Infrastructure;

// Pomocnicze operacje taksonomii: kategorie i umiejętności są globalne, więc testy tworzą własne, unikalne
// (token w nazwie / slugu) i zawężają wyniki list do nich
public static class TaxonomyScenario
{
    // Krótki unikalny token do nazw i slugów (małe litery i cyfry - poprawny slug)
    public static string Token() => Guid.NewGuid().ToString("N")[..10];

    public static async Task<CategoryDto> CreateCategoryAsync(this ApiFixture api, string? token = null, int sortOrder = 0)
    {
        token ??= Token();
        using var admin = await api.AdminClientAsync();
        var response = await admin.PostJsonAsync("/api/admin/categories",
            new CategoryFormDto { Name = "Kategoria " + token, Slug = "kat-" + token, SortOrder = sortOrder });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<CategoryDto>();
    }

    public static async Task<(HttpResponseMessage Response, JobOfferDto[] Items)> GetJobsAsync(
        this ApiFixture api, string query, HttpClient? client = null)
    {
        var own = client == null;
        client ??= api.CreateClient();
        try
        {
            var response = await client.GetAsync("/api/jobs" + query);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            return (response, await response.ReadJsonAsync<JobOfferDto[]>());
        }
        finally
        {
            if (own)
            {
                client.Dispose();
            }
        }
    }

    public static async Task<(HttpResponseMessage Response, JobOfferDto[] Items)> GetManagedAsync(
        this ApiFixture api, HttpClient client, string query)
    {
        var response = await client.GetAsync("/api/jobs/manage" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (response, await response.ReadJsonAsync<JobOfferDto[]>());
    }

    public static async Task<JobFacetsDto> GetFacetsAsync(this ApiFixture api, string query)
    {
        using var client = api.CreateClient();
        var response = await client.GetAsync("/api/jobs/facets" + query);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<JobFacetsDto>();
    }

    // Zapytanie z parametrami zakodowanymi do adresu
    public static string Query(params (string Name, string? Value)[] parameters) =>
        "?" + string.Join("&", parameters.Where(p => p.Value != null)
            .Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value!)}"));
}
