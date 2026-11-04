using System.Net.Http.Json;
using System.Text.Json;
using Backend.Helpers;

namespace Backend.Tests.Infrastructure;

// Pomocnicze metody do JSON-a i nagłówka paginacji
public static class ApiJson
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    public static Task<HttpResponseMessage> PostJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PostAsJsonAsync(url, body, Options);

    public static Task<HttpResponseMessage> PutJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.PutAsJsonAsync(url, body, Options);

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Options);
        Assert.NotNull(value);
        return value!;
    }

    // Parsuje nagłówek Pagination i sprawdza, że ma dokładnie pola z kontraktu (PascalCase)
    public static PaginationHeader ReadPagination(this HttpResponseMessage response)
    {
        Assert.True(response.Headers.TryGetValues("Pagination", out var values), "Missing Pagination header");
        var raw = Assert.Single(values!);

        using var document = JsonDocument.Parse(raw);
        var names = document.RootElement.EnumerateObject().Select(p => p.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "CurrentPage", "ItemsPerPage", "TotalItems", "TotalPages" }, names);

        var header = JsonSerializer.Deserialize<PaginationHeader>(raw);
        Assert.NotNull(header);
        return header!;
    }
}
