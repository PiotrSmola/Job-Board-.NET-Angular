using System.Net.Http.Json;
using System.Text;
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

    // Surowy JSON (np. wartości spoza enuma albo daty z różnymi strefami, których nie da się zbudować z DTO)
    public static Task<HttpResponseMessage> PostRawJsonAsync(this HttpClient client, string url, string json) =>
        client.PostAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PutRawJsonAsync(this HttpClient client, string url, string json) =>
        client.PutAsync(url, new StringContent(json, Encoding.UTF8, "application/json"));

    public static Task<HttpResponseMessage> PatchJsonAsync<T>(this HttpClient client, string url, T body) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = JsonContent.Create(body, options: Options)
        });

    public static Task<HttpResponseMessage> PatchRawJsonAsync(this HttpClient client, string url, string json) =>
        client.SendAsync(new HttpRequestMessage(HttpMethod.Patch, url)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        });

    public static async Task<T> ReadJsonAsync<T>(this HttpResponseMessage response)
    {
        var value = await response.Content.ReadFromJsonAsync<T>(Options);
        Assert.NotNull(value);
        return value!;
    }

    // Pole "message" z odpowiedzi błędu API (np. new { message = "..." })
    public static async Task<string?> ReadMessageAsync(this HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("message").GetString();
    }

    // Nazwy pól z błędami walidacji (ValidationProblem: "errors")
    public static async Task<string[]> ReadValidationFieldsAsync(this HttpResponseMessage response)
    {
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return body.RootElement.GetProperty("errors").EnumerateObject().Select(p => p.Name).ToArray();
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
