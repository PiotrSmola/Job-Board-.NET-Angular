using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Backend.Tests.Integration;

// Dokumenty profilu kandydata (/api/account/documents): walidacja i magazyn jak przy załącznikach aplikacji,
// limity profilu (10 dokumentów, 25 MB), 201 z pełną listą, tylko właściciel, tylko rola kandydata
[Collection(ApiCollection.Name)]
public class ProfileDocumentTests
{
    private const string NotFound = "Document not found";

    private readonly ApiFixture _api;

    public ProfileDocumentTests(ApiFixture api) => _api = api;

    private Task<List<string>> StoredKeysAsync(string userId) =>
        _api.QueryDbAsync(db => db.ProfileDocuments.Where(d => d.UserId == userId).OrderBy(d => d.Id).Select(d => d.FileKey).ToListAsync());

    private string StoragePath(string key) => Path.Combine(_api.Factory.StoragePath, key.Replace('/', Path.DirectorySeparatorChar));

    private string[] StoredFilesOf(string userId)
    {
        var directory = Path.Combine(_api.Factory.StoragePath, "profile-documents", userId);
        return Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
    }

    [Fact]
    public async Task Upload_Returns201WithTheFullSortedListAndStoresTheFiles()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        await client.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "list.pdf", AttachmentKind.CoverLetter));

        var response = await client.UploadDocumentsAsync(
            new AttachmentUpload(AttachmentTestFiles.Docx(), "../../CV Jan.docx", AttachmentKind.Cv, AttachmentTestFiles.DocxContentType),
            new AttachmentUpload(AttachmentTestFiles.Png(), "zdjecie.jpg", AttachmentKind.Other, AttachmentTestFiles.PngContentType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var documents = await response.ReadJsonAsync<List<ProfileDocumentDto>>();
        // pełna lista posortowana jak załączniki: rodzaj, data dodania
        Assert.Equal(["CV Jan.docx", "list.pdf", "zdjecie.png"], documents.Select(d => d.FileName));
        Assert.Equal([AttachmentKind.Cv, AttachmentKind.CoverLetter, AttachmentKind.Other], documents.Select(d => d.Kind));
        Assert.Equal(AttachmentTestFiles.Docx().Length, documents[0].Size);

        var keys = await StoredKeysAsync(user.Id);
        Assert.All(keys, key => Assert.Matches($"^profile-documents/{user.Id}/[0-9a-f]{{32}}\\.(pdf|docx|png)$", key));
        Assert.Equal(keys.Select(k => Path.GetFullPath(StoragePath(k))).Order(), StoredFilesOf(user.Id).Select(Path.GetFullPath).Order());

        var list = await (await client.GetAsync(AccountApi.DocumentsUrl)).ReadJsonAsync<List<ProfileDocumentDto>>();
        Assert.Equivalent(documents, list);
    }

    [Fact]
    public async Task Documents_DtoHasNoStorageData()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        await client.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv));

        using var body = JsonDocument.Parse(await (await client.GetAsync(AccountApi.DocumentsUrl)).Content.ReadAsStringAsync());

        Assert.Equal(["fileName", "id", "kind", "size", "uploadedAt"],
            body.RootElement[0].EnumerateObject().Select(p => p.Name).Order().ToArray());
    }

    [Fact]
    public async Task Download_ReturnsTheFileWithSafeHeaders()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        var content = AttachmentTestFiles.Pdf(100);
        var documents = await client.UploadDocumentsOkAsync(new AttachmentUpload(content, "Życiorys Jan?.pdf", AttachmentKind.Cv));

        var response = await client.GetAsync(AccountApi.DocumentUrl(documents[0].Id));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(content, await response.Content.ReadAsByteArrayAsync());
        Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition?.DispositionType);
        Assert.Equal("Życiorys Jan_.pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
        Assert.Equal(content.Length, response.Content.Headers.ContentLength);
        Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
        Assert.True(response.Headers.CacheControl?.NoStore);
    }

    [Fact]
    public async Task Delete_RemovesTheRecordAndTheFile_ThenReturns404()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        var documents = await client.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv));
        var key = Assert.Single(await StoredKeysAsync(user.Id));

        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync(AccountApi.DocumentUrl(documents[0].Id))).StatusCode);

        Assert.False(File.Exists(StoragePath(key)));
        Assert.Empty(await StoredKeysAsync(user.Id));
        var again = await client.DeleteAsync(AccountApi.DocumentUrl(documents[0].Id));
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(NotFound, await again.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync(AccountApi.DocumentUrl(documents[0].Id))).StatusCode);
    }

    [Fact]
    public async Task OtherCandidatesDocument_LooksLikeItDoesNotExist()
    {
        var owner = await _api.RegisterUserAsync("docs");
        using var ownerClient = _api.CreateClient(owner);
        var documents = await ownerClient.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv));
        var stranger = await _api.RegisterUserAsync("docs");
        using var strangerClient = _api.CreateClient(stranger);

        var download = await strangerClient.GetAsync(AccountApi.DocumentUrl(documents[0].Id));
        var delete = await strangerClient.DeleteAsync(AccountApi.DocumentUrl(documents[0].Id));

        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal(NotFound, await download.ReadMessageAsync());
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
        Assert.Empty(await (await strangerClient.GetAsync(AccountApi.DocumentsUrl)).ReadJsonAsync<List<ProfileDocumentDto>>());
        Assert.Single(await StoredKeysAsync(owner.Id));
    }

    [Fact]
    public async Task Documents_AreForbiddenForEmployersAndAdminsAndRequireAuthentication()
    {
        var employer = await _api.RegisterEmployerAsync();
        using var employerClient = _api.CreateClient(employer);
        using var admin = await _api.AdminClientAsync();
        using var anonymous = _api.CreateClient();

        foreach (var client in new[] { employerClient, admin })
        {
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(AccountApi.DocumentsUrl)).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.UploadDocumentsAsync(
                new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf"))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.GetAsync(AccountApi.DocumentUrl(1))).StatusCode);
            Assert.Equal(HttpStatusCode.Forbidden, (await client.DeleteAsync(AccountApi.DocumentUrl(1))).StatusCode);
        }

        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(AccountApi.DocumentsUrl)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.UploadDocumentsAsync(
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "cv.pdf"))).StatusCode);
    }

    [Theory]
    [InlineData("gif", "Unsupported file type - upload a PDF, DOCX, PNG or JPEG file")]
    [InlineData("empty", "File is empty")]
    [InlineData("large", "File is too large - maximum size is 5 MB")]
    public async Task Upload_WithAnInvalidFile_Returns400AndStoresNothing(string variant, string message)
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        var content = variant switch
        {
            "gif" => AttachmentTestFiles.Gif(),
            "empty" => [],
            _ => AttachmentTestFiles.PdfOfSize(5 * 1024 * 1024 + 1)
        };

        // poprawny plik w tym samym żądaniu też nie zostaje zapisany (wszystko albo nic)
        var response = await client.UploadDocumentsAsync(
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "ok.pdf"),
            new AttachmentUpload(content, "bad.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await response.ReadMessageAsync());
        Assert.Empty(await StoredKeysAsync(user.Id));
        Assert.Empty(StoredFilesOf(user.Id));
    }

    [Fact]
    public async Task Upload_WithoutFiles_Returns400()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);

        var response = await client.PostAsync(AccountApi.DocumentsUrl, new MultipartFormDataContent { { new StringContent("0"), "kinds" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("No file uploaded - send files in the 'files' form field", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Upload_OverTenDocuments_Returns400WithTheProfileMessage()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        await client.UploadDocumentsOkAsync(Enumerable.Range(0, 9)
            .Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(), $"d{i}.pdf")).ToArray());

        var tooMany = await client.UploadDocumentsAsync(
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "a.pdf"), new AttachmentUpload(AttachmentTestFiles.Pdf(), "b.pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, tooMany.StatusCode);
        Assert.Equal("A profile can have at most 10 documents", await tooMany.ReadMessageAsync());

        await client.UploadDocumentsOkAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "tenth.pdf"));

        // pełny profil - odrzucenie przed odczytem plików
        var full = await client.UploadDocumentsAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "eleventh.pdf"));
        Assert.Equal(HttpStatusCode.BadRequest, full.StatusCode);
        Assert.Equal("A profile can have at most 10 documents", await full.ReadMessageAsync());
        Assert.Equal(10, (await StoredKeysAsync(user.Id)).Count);
        Assert.Equal(10, StoredFilesOf(user.Id).Length);
    }

    [Fact]
    public async Task Upload_OverElevenFilesInOneRequest_Returns400WithTheProfileMessage()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);

        var response = await client.UploadDocumentsAsync(Enumerable.Range(0, 11)
            .Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(), $"d{i}.pdf", AttachmentKind.Other)).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("A profile can have at most 10 documents", await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Upload_Over25MegabytesInTotal_Returns400WithTheProfileMessage()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        var fiveMegabytes = AttachmentTestFiles.PdfOfSize(5 * 1024 * 1024);
        await client.UploadDocumentsOkAsync(Enumerable.Range(0, 5).Select(i => new AttachmentUpload(fiveMegabytes, $"d{i}.pdf")).ToArray());

        var response = await client.UploadDocumentsAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), "one-more.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("Total size of profile documents cannot exceed 25 MB", await response.ReadMessageAsync());
        Assert.Equal(5, StoredFilesOf(user.Id).Length);
    }

    [Fact]
    public async Task ConcurrentUploads_NeverExceedTheLimit()
    {
        var user = await _api.RegisterUserAsync("docs");
        using var client = _api.CreateClient(user);
        await client.UploadDocumentsOkAsync(Enumerable.Range(0, 8)
            .Select(i => new AttachmentUpload(AttachmentTestFiles.Pdf(), $"d{i}.pdf")).ToArray());

        var responses = await Task.WhenAll(Enumerable.Range(0, 4).Select(i =>
            client.UploadDocumentsAsync(new AttachmentUpload(AttachmentTestFiles.Pdf(), $"p{i}.pdf"))));

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        Assert.Equal(10, (await StoredKeysAsync(user.Id)).Count);
        // pliki odrzuconych żądań usunięte
        Assert.Equal(10, StoredFilesOf(user.Id).Length);
    }
}
