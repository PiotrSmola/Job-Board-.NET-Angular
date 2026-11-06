using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Zgodność z klientem Angulara (JobApplicationService.uploadAttachments / downloadAttachment, getApiErrorMessage):
// żądanie multipart dokładnie w kształcie, jaki buduje frontend, kształt JSON odpowiedzi i błędów oraz nagłówki
[Collection(ApiCollection.Name)]
public class AttachmentFrontendContractTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentFrontendContractTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    // FormData z frontendu: dla każdego pliku po kolei pole "files" (z nazwą), potem pole "kinds" z liczbą
    // (formData.append('files', file, file.name); formData.append('kinds', String(kind)))
    private static MultipartFormDataContent FrontendForm(params AttachmentUpload[] items)
    {
        var form = new MultipartFormDataContent();
        foreach (var item in items)
        {
            var file = new ByteArrayContent(item.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(item.ContentType);
            form.Add(file, "files", item.FileName);
            form.Add(new StringContent(((int)(item.Kind ?? AttachmentKind.Other)).ToString()), "kinds");
        }

        return form;
    }

    private static AttachmentUpload Pdf(int index, AttachmentKind kind) =>
        new(AttachmentTestFiles.Pdf(index), $"plik{index}.pdf", kind);

    private async Task<HttpResponseMessage> PostFrontendFormAsync(AttachmentSetup setup, params AttachmentUpload[] items)
    {
        using var client = _api.CreateClient(setup.Candidate);
        return await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), FrontendForm(items));
    }

    // --- kształt żądania ---

    [Fact]
    public async Task Upload_InterleavedFilesAndKindsInTheFrontendOrder_AssignsEachKindToItsFile()
    {
        var setup = await _attachments.NewSetupAsync();

        // rodzaje celowo nie w kolejności enuma: i-te pole "kinds" należy do i-tego pliku
        var response = await PostFrontendFormAsync(setup,
            new AttachmentUpload(AttachmentTestFiles.Png(), "zdjecie.png", AttachmentKind.Portfolio, AttachmentTestFiles.PngContentType),
            new AttachmentUpload(AttachmentTestFiles.Pdf(), "zyciorys.pdf", AttachmentKind.Cv),
            new AttachmentUpload(AttachmentTestFiles.Docx(), "list.docx", AttachmentKind.CoverLetter, AttachmentTestFiles.DocxContentType),
            new AttachmentUpload(AttachmentTestFiles.Jpeg(), "inne.jpeg", AttachmentKind.Other, AttachmentTestFiles.JpegContentType));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(AttachmentKind.Portfolio, records.Single(r => r.Name == "zdjecie.png").Kind);
        Assert.Equal(AttachmentKind.Cv, records.Single(r => r.Name == "zyciorys.pdf").Kind);
        Assert.Equal(AttachmentKind.CoverLetter, records.Single(r => r.Name == "list.docx").Kind);
        // .jpeg wysłane przez klienta serwer zapisuje jako .jpg (rozszerzenie wynika z zawartości)
        Assert.Equal(AttachmentKind.Other, records.Single(r => r.Name == "inne.jpg").Kind);

        // odpowiedź jest posortowana wg rodzaju - klient pokazuje ją bez własnego sortowania
        var dto = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal([AttachmentKind.Cv, AttachmentKind.CoverLetter, AttachmentKind.Portfolio, AttachmentKind.Other],
            dto.Attachments.Select(a => a.Kind));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    public async Task Upload_FrontendShapeWithUpToFiveFiles_IsAccepted(int count)
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await PostFrontendFormAsync(setup,
            Enumerable.Range(0, count).Select(i => Pdf(i, (AttachmentKind)(i % 4))).ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(count, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
    }

    // pola "kinds" liczą się do limitu części formularza razem z plikami - 10 par plik + rodzaj musi się zmieścić
    [Theory]
    [InlineData(6)]
    [InlineData(10)]
    public async Task Upload_FrontendShapeWithSixToTenFiles_IsAccepted(int count)
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await PostFrontendFormAsync(setup,
            Enumerable.Range(0, count).Select(i => Pdf(i, (AttachmentKind)(i % 4))).ToArray());

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(count, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
    }

    // 11. plik z polem kinds dostaje komunikat o limicie 10 załączników, a nie błąd formularza
    [Fact]
    public async Task Upload_FrontendShapeWithElevenFiles_ReturnsTooManyAttachmentsMessage()
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await PostFrontendFormAsync(setup,
            Enumerable.Range(0, AttachmentFiles.MaxAttachments + 1).Select(i => Pdf(i, AttachmentKind.Other)).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TooManyMessage, await response.ReadMessageAsync());
    }

    // limit części nadal ciasny: 12 par plik + rodzaj przekracza go, zanim formularz zostanie zbuforowany do końca
    [Fact]
    public async Task Upload_FrontendShapeWithTwelveFiles_IsRejectedAsInvalidForm()
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await PostFrontendFormAsync(setup,
            Enumerable.Range(0, AttachmentFiles.MaxAttachments + 2).Select(i => Pdf(i, AttachmentKind.Other)).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.InvalidFormMessage, await response.ReadMessageAsync());
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    // --- kształt odpowiedzi ---

    [Fact]
    public async Task Upload_ResponseJson_HasExactlyTheFieldsTheFrontendModelExpects()
    {
        var setup = await _attachments.NewSetupAsync();

        var response = await PostFrontendFormAsync(setup, Pdf(1, AttachmentKind.CoverLetter));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var root = body.RootElement;

        var totalSize = root.GetProperty("attachmentsTotalSize");
        Assert.Equal(JsonValueKind.Number, totalSize.ValueKind);
        Assert.Equal(AttachmentTestFiles.Pdf(1).Length, totalSize.GetInt64());
        Assert.False(root.TryGetProperty("hasCv", out _));
        Assert.False(root.TryGetProperty("cvFileName", out _));

        var attachment = Assert.Single(root.GetProperty("attachments").EnumerateArray());
        Assert.Equal(["fileName", "id", "kind", "size", "uploadedAt"],
            attachment.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        // rodzaj to liczba (enum AttachmentKind we frontendzie), nie nazwa
        Assert.Equal(JsonValueKind.Number, attachment.GetProperty("kind").ValueKind);
        Assert.Equal((int)AttachmentKind.CoverLetter, attachment.GetProperty("kind").GetInt32());
        Assert.Equal(JsonValueKind.Number, attachment.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.Number, attachment.GetProperty("size").ValueKind);
        Assert.Equal("plik1.pdf", attachment.GetProperty("fileName").GetString());
        // data w UTC z końcówką Z (frontend parsuje ją jako moment w czasie)
        Assert.EndsWith("Z", attachment.GetProperty("uploadedAt").GetString());
    }

    [Fact]
    public async Task ApplicationWithoutAttachments_ReturnsEmptyListAndZeroSizeInEveryEndpoint()
    {
        var setup = await _attachments.NewSetupAsync();
        using var candidate = _api.CreateClient(setup.Candidate);
        using var admin = await _api.AdminClientAsync();

        foreach (var (client, url) in new[]
                 {
                     (candidate, $"/api/jobapplications/{setup.Application.Id}"),
                     (candidate, "/api/jobapplications/my-applications"),
                     (admin, "/api/jobapplications/all?pageSize=50"),
                     (admin, $"/api/jobapplications/all?jobOfferId={setup.Job.Id}")
                 })
        {
            var response = await client.GetAsync(url);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var item = body.RootElement.ValueKind == JsonValueKind.Array
                ? body.RootElement.EnumerateArray().First(e => e.GetProperty("id").GetInt32() == setup.Application.Id)
                : body.RootElement;

            // pusta lista (nie null) - szablony frontendu iterują po attachments bez sprawdzania null
            Assert.Equal(JsonValueKind.Array, item.GetProperty("attachments").ValueKind);
            Assert.Equal(0, item.GetProperty("attachments").GetArrayLength());
            Assert.Equal(0, item.GetProperty("attachmentsTotalSize").GetInt64());
        }
    }

    // --- kształt błędów (getApiErrorMessage czyta wyłącznie pole "message") ---

    [Fact]
    public async Task UploadRejections_AreJsonObjectsWithOnlyTheMessageFieldThatFrontendTranslates()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);
        var url = AttachmentTestFiles.Url(setup.Application.Id);

        var badKind = FrontendForm(Pdf(0, AttachmentKind.Cv));
        badKind.Add(new StringContent("9"), "kinds");
        var noFiles = new MultipartFormDataContent { { new StringContent("0"), "kinds" } };
        var emptyFile = FrontendForm(new AttachmentUpload([], "pusty.pdf", AttachmentKind.Cv));
        var gif = FrontendForm(new AttachmentUpload(AttachmentTestFiles.Gif(), "a.pdf", AttachmentKind.Cv));
        var tooBig = FrontendForm(new AttachmentUpload(AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize + 1), "duzy.pdf", AttachmentKind.Cv));

        var cases = new (HttpContent Content, string Message)[]
        {
            (badKind, AttachmentFiles.InvalidKindMessage),
            (noFiles, AttachmentFiles.NoFileMessage),
            (emptyFile, AttachmentFiles.EmptyFileMessage),
            (gif, AttachmentFiles.UnsupportedTypeMessage),
            (tooBig, AttachmentFiles.TooLargeMessage)
        };

        foreach (var (content, message) in cases)
        {
            var response = await client.PostAsync(url, content);

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
            using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(["message"], body.RootElement.EnumerateObject().Select(p => p.Name));
            Assert.Equal(message, body.RootElement.GetProperty("message").GetString());
        }
    }

    [Fact]
    public async Task UploadInLockedStatusAndMissingAttachment_ReturnJsonMessagesKnownToTheFrontend()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var missing = await client.DeleteAttachmentAsync(setup.Application.Id, 999_999);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("application/json", missing.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AttachmentScenario.AttachmentNotFound, await missing.ReadMessageAsync());

        var download = await client.DownloadAttachmentAsync(setup.Application.Id, 999_999);
        Assert.Equal(HttpStatusCode.NotFound, download.StatusCode);
        Assert.Equal("application/json", download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(AttachmentScenario.AttachmentNotFound, await download.ReadMessageAsync());

        await _attachments.SetStatusAsync(setup, ApplicationStatus.Withdrawn);
        var locked = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), FrontendForm(Pdf(0, AttachmentKind.Cv)));
        Assert.Equal(HttpStatusCode.BadRequest, locked.StatusCode);
        Assert.Equal(AttachmentScenario.StatusMessage, await locked.ReadMessageAsync());
    }

    // --- pobieranie ---

    [Fact]
    public async Task Download_FromTheAngularDevOrigin_ExposesContentDispositionToTheBrowser()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "Życiorys.pdf");

        // ng serve (http://localhost:4200) woła API cross-origin - bez Expose-Headers przeglądarka ukrywa Content-Disposition
        // i frontend używa wtedy tylko nazwy z DTO
        using var client = _api.CreateClient(setup.Candidate);
        using var request = new HttpRequestMessage(HttpMethod.Get, AttachmentTestFiles.Url(setup.Application.Id, attachment.Id));
        request.Headers.Add("Origin", "http://localhost:4200");
        var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("http://localhost:4200", string.Join(",", response.Headers.GetValues("Access-Control-Allow-Origin")));
        var exposed = string.Join(",", response.Headers.GetValues("Access-Control-Expose-Headers"));
        Assert.Contains("Content-Disposition", exposed, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Download_ContentDisposition_UsesOnlyFilenameStarSoTheFrontendDecodesUtf8()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Png(),
            "Zażółć gęślą jaźń; (1).png");

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);

        var header = string.Join(",", response.Content.Headers.GetValues("Content-Disposition"));
        Assert.StartsWith("attachment; filename*=UTF-8''", header);
        Assert.DoesNotContain("filename=", header.Replace("filename*=", string.Empty));
        // wartość jest zakodowana procentowo, bez surowych znaków spoza ASCII, średników i spacji
        var encoded = header["attachment; filename*=UTF-8''".Length..];
        Assert.All(encoded, c => Assert.True(c is < '\u007f' and not ' ' and not ';' and not '"'));
        Assert.Equal("Zażółć gęślą jaźń; (1).png", Uri.UnescapeDataString(encoded));
    }
}
