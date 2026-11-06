using System.Net;
using System.Text;
using Backend.Helpers;
using Backend.Services;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Limit treści żądania (RequestBodyLimitMiddleware): domyślnie 1 MB dla całego API, upload załączników 26 MB.
// TestServer nie obsługuje limitu serwera (IHttpMaxRequestBodySizeFeature), więc testy sprawdzają limit niezależny
// od serwera - strumień z limitem; w Kestrelu działa dodatkowo globalny MaxRequestBodySize = 1 MB.
[Collection(ApiCollection.Name)]
public class RequestBodyLimitTests
{
    private const string LoginUrl = "/api/auth/login";

    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public RequestBodyLimitTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // JSON logowania z hasłem dobranym tak, żeby całe ciało miało dokładnie size bajtów
    private static byte[] LoginJson(long size)
    {
        const string prefix = "{\"email\":\"jan.kowalski@example.com\",\"password\":\"";
        const string suffix = "\"}";
        return Encoding.UTF8.GetBytes(prefix + new string('a', (int)size - prefix.Length - suffix.Length) + suffix);
    }

    private static HttpContent Json(byte[] body, bool chunked)
    {
        // StreamContent bez długości - żądanie bez Content-Length (chunked), limit działa w trakcie czytania
        HttpContent content = chunked ? new StreamContent(new NonSeekableStream(body)) : new ByteArrayContent(body);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        return content;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LoginWithABodyOverOneMegabyte_Returns413WithMessage(bool chunked)
    {
        using var client = _api.CreateClient();

        var response = await client.PostAsync(LoginUrl, Json(LoginJson(RequestBodyLimitMiddleware.DefaultLimit + 1), chunked));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal(GlobalExceptionHandler.RequestTooLargeMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task LoginWithABodyOfExactlyOneMegabyte_IsProcessed()
    {
        using var client = _api.CreateClient();

        var response = await client.PostAsync(LoginUrl, Json(LoginJson(RequestBodyLimitMiddleware.DefaultLimit), chunked: false));

        // zbyt długie hasło - zwykła odpowiedź API (walidacja albo złe dane logowania), nie 413
        Assert.NotEqual(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.True((int)response.StatusCode is 400 or 401, $"Unexpected status {(int)response.StatusCode}");
    }

    [Fact]
    public async Task UploadEndpoint_KeepsItsOwnLimitAboveOneMegabyte()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        // trzy pliki po 5 MB w jednym żądaniu - ponad domyślny limit, w limicie endpointu (26 MB)
        var response = await client.UploadAttachmentsAsync(setup.Application.Id,
            Enumerable.Range(0, 3).Select(i => new AttachmentUpload(
                AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize - i), $"duzy{i}.pdf")).ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // Strumień bez długości i bez przewijania - HttpClient wysyła go jako chunked
    private sealed class NonSeekableStream(byte[] content) : MemoryStream(content)
    {
        public override bool CanSeek => false;
    }
}
