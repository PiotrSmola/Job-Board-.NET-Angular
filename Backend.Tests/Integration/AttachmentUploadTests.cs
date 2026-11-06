using System.Net;
using System.Text.Json;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Models;
using Backend.Tests.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Integration;

// Załączniki aplikacji: POST/DELETE/GET /api/jobapplications/{id}/attachments, walidacja plików, limity, uprawnienia
// i sprzątanie plików (magazyn Local w katalogu tymczasowym fabryki)
[Collection(ApiCollection.Name)]
public class AttachmentUploadTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentUploadTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
    }

    private static AttachmentUpload File(byte[] content, string name, AttachmentKind? kind = null) => new(content, name, kind);

    // --- upload ---

    [Fact]
    public async Task Upload_Pdf_Returns201WithAttachmentAndStoresFileUnderServerKey()
    {
        var setup = await _attachments.NewSetupAsync();
        var pdf = AttachmentTestFiles.Pdf();
        var before = DateTime.UtcNow.AddSeconds(-5);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, pdf, "../../etc/Moje CV.pdf", kind: AttachmentKind.Cv);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal($"/api/JobApplications/{setup.Application.Id}", response.Headers.Location?.AbsolutePath, ignoreCase: true);
        var json = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("fileKey", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attachments/", json);

        var dto = JsonSerializer.Deserialize<JobApplicationDto>(json, ApiJson.Options)!;
        var attachment = Assert.Single(dto.Attachments);
        Assert.Equal(AttachmentKind.Cv, attachment.Kind);
        Assert.Equal("Moje CV.pdf", attachment.FileName);
        Assert.Equal(pdf.Length, attachment.Size);
        Assert.InRange(attachment.UploadedAt, before, DateTime.UtcNow.AddSeconds(5));
        Assert.Equal(pdf.Length, dto.AttachmentsTotalSize);
        Assert.NotNull(dto.History);

        var record = Assert.Single(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Equal(attachment.Id, record.Id);
        Assert.Matches($"^attachments/{setup.Application.Id}/[0-9a-f]{{32}}\\.pdf$", record.Key);
        Assert.Equal(AttachmentTestFiles.PdfContentType, record.ContentType);
        Assert.Equal(pdf, await System.IO.File.ReadAllBytesAsync(_attachments.StoragePath(record.Key)));
    }

    [Fact]
    public async Task Upload_ThreeFilesOfDifferentKindsInOneRequest_StoresAllSortedByKind()
    {
        var setup = await _attachments.NewSetupAsync();
        var pdf = AttachmentTestFiles.Pdf(paddingBytes: 10);
        var docx = AttachmentTestFiles.Docx();
        var png = AttachmentTestFiles.Png(paddingBytes: 100);

        var dto = await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            File(png, "portfolio.png", AttachmentKind.Portfolio),
            File(docx, "list.docx", AttachmentKind.CoverLetter),
            File(pdf, "cv.pdf", AttachmentKind.Cv));

        Assert.Equal(new[] { AttachmentKind.Cv, AttachmentKind.CoverLetter, AttachmentKind.Portfolio }, dto.Attachments.Select(a => a.Kind));
        Assert.Equal(new[] { "cv.pdf", "list.docx", "portfolio.png" }, dto.Attachments.Select(a => a.FileName));
        Assert.Equal(pdf.Length + docx.Length + png.Length, dto.AttachmentsTotalSize);

        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(3, records.Count);
        Assert.Equal(new[] { AttachmentTestFiles.PngContentType, AttachmentTestFiles.DocxContentType, AttachmentTestFiles.PdfContentType },
            records.Select(r => r.ContentType));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task Upload_WithoutKinds_StoresOther_AndFewerKindsThanFilesFillTheRestWithOther()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var noKinds = await client.UploadAttachmentsAsync(setup.Application.Id, File(AttachmentTestFiles.Pdf(), "a.pdf"));
        Assert.Equal(HttpStatusCode.Created, noKinds.StatusCode);
        Assert.Equal(AttachmentKind.Other, Assert.Single((await noKinds.ReadJsonAsync<JobApplicationDto>()).Attachments).Kind);

        // dwa pliki, jedno pole kinds - drugi plik dostaje Other
        var form = AttachmentTestFiles.Form(File(AttachmentTestFiles.Pdf(1), "b.pdf"), File(AttachmentTestFiles.Pdf(2), "c.pdf"));
        form.Add(new StringContent("1"), "kinds");
        var partial = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), form);

        Assert.Equal(HttpStatusCode.Created, partial.StatusCode);
        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(AttachmentKind.CoverLetter, records.Single(r => r.Name == "b.pdf").Kind);
        Assert.Equal(AttachmentKind.Other, records.Single(r => r.Name == "c.pdf").Kind);
    }

    [Theory]
    [InlineData("4")]
    [InlineData("-1")]
    [InlineData("cv")]
    [InlineData("1.0")]
    [InlineData(" 1")]
    [InlineData("")]
    [InlineData("99999999999")]
    public async Task Upload_InvalidKind_ReturnsBadRequestAndStoresNothing(string kind)
    {
        var setup = await _attachments.NewSetupAsync();
        var form = AttachmentTestFiles.Form(File(AttachmentTestFiles.Pdf(), "cv.pdf"));
        form.Add(new StringContent(kind), "kinds");

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.InvalidKindMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_MoreKindsThanFiles_ReturnsBadRequest()
    {
        var setup = await _attachments.NewSetupAsync();
        var form = AttachmentTestFiles.Form(File(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv));
        form.Add(new StringContent("1"), "kinds");

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.InvalidKindMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_DocxWithPdfExtension_IsStoredAsDocx()
    {
        var setup = await _attachments.NewSetupAsync();

        var dto = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Docx(), "cv.pdf");

        Assert.Equal("cv.docx", Assert.Single(dto.Attachments).FileName);
        Assert.Equal(AttachmentTestFiles.DocxContentType, Assert.Single(await _attachments.RecordsAsync(setup.Application.Id)).ContentType);
    }

    [Theory]
    [InlineData("photo.png", "png", "photo.png", "image/png")]
    [InlineData("photo.jpeg", "jpeg", "photo.jpg", "image/jpeg")]
    [InlineData("photo.png", "jpeg", "photo.jpg", "image/jpeg")]
    [InlineData("scan.pdf", "png", "scan.png", "image/png")]
    public async Task Upload_Images_AreDetectedByContent(string sent, string format, string expectedName, string expectedType)
    {
        var setup = await _attachments.NewSetupAsync();
        var content = format == "png" ? AttachmentTestFiles.Png(paddingBytes: 50) : AttachmentTestFiles.Jpeg(paddingBytes: 50);

        var dto = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, content, sent, AttachmentKind.Portfolio);

        var attachment = Assert.Single(dto.Attachments);
        Assert.Equal(expectedName, attachment.FileName);
        var record = Assert.Single(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Equal(expectedType, record.ContentType);
        Assert.EndsWith(Path.GetExtension(expectedName), record.Key);

        using var client = _api.CreateClient(setup.Employer);
        var download = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
        Assert.Equal(expectedType, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(content, await download.Content.ReadAsByteArrayAsync());
    }

    public static TheoryData<string, byte[]> UnsupportedFiles => new()
    {
        { "gif.pdf", AttachmentTestFiles.Gif() },
        { "archive.docx", AttachmentTestFiles.Zip(("readme.txt", "hello")) },
        { "sheet.docx", AttachmentTestFiles.Zip(("[Content_Types].xml", "<Types/>"), ("xl/workbook.xml", "<workbook/>")) },
        { "broken.docx", [0x50, 0x4B, 0x03, 0x04, 0x01, 0x02, 0x03] },
        { "text.pdf", "just text, not a PDF"u8.ToArray() }
    };

    [Theory]
    [MemberData(nameof(UnsupportedFiles))]
    public async Task Upload_UnsupportedContent_ReturnsBadRequestAndStoresNothing(string fileName, byte[] content)
    {
        var setup = await _attachments.NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, content, fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.UnsupportedTypeMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_EmptyFile_ReturnsBadRequest()
    {
        var setup = await _attachments.NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, []);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.EmptyFileMessage, await response.ReadMessageAsync());
    }

    [Theory]
    [InlineData(AttachmentFiles.MaxFileSize + 1, AttachmentFiles.TooLargeMessage)]
    [InlineData(6 * 1024 * 1024, AttachmentFiles.TooLargeMessage)]
    // żądanie większe niż 26 MB jest odrzucane po Content-Length, bez czytania treści
    [InlineData(AttachmentFiles.MaxRequestSize + 1, AttachmentFiles.TotalTooLargeMessage)]
    public async Task Upload_TooLargeFile_ReturnsBadRequest(long size, string message)
    {
        var setup = await _attachments.NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.PdfOfSize(size));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await response.ReadMessageAsync());
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_FileOfExactlyMaxSize_IsAccepted()
    {
        var setup = await _attachments.NewSetupAsync();

        var dto = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id,
            AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize), "big.pdf");

        Assert.Equal(AttachmentFiles.MaxFileSize, Assert.Single(dto.Attachments).Size);
    }

    [Fact]
    public async Task Upload_WithoutFilesField_ReturnsNoFileMessage()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        // plik w innym polu, sam formularz bez plików i żądanie bez formularza
        var wrongField = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id),
            AttachmentTestFiles.Form("file", File(AttachmentTestFiles.Pdf(), "cv.pdf")));
        var onlyKinds = new MultipartFormDataContent { { new StringContent("0"), "kinds" } };
        var noFiles = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), onlyKinds);
        var json = await client.PostJsonAsync(AttachmentTestFiles.Url(setup.Application.Id), new { files = "x" });

        foreach (var response in new[] { wrongField, noFiles, json })
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(AttachmentFiles.NoFileMessage, await response.ReadMessageAsync());
        }

        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_TruncatedMultipart_ReturnsInvalidFormAndStoresNothing()
    {
        var setup = await _attachments.NewSetupAsync();
        const string boundary = "truncated-boundary";
        var body = System.Text.Encoding.ASCII.GetBytes(
            $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; filename=\"cv.pdf\"\r\n" +
            "Content-Type: application/pdf\r\n\r\n");
        // treść pliku bez końcowej granicy - klient zerwał wysyłanie
        var content = new ByteArrayContent([.. body, .. AttachmentTestFiles.Pdf(paddingBytes: 5000)]);
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.InvalidFormMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_GarbageWithMultipartContentType_ReturnsInvalidForm()
    {
        var setup = await _attachments.NewSetupAsync();
        var content = new ByteArrayContent("this is not multipart at all"u8.ToArray());
        content.Headers.ContentType = System.Net.Http.Headers.MediaTypeHeaderValue.Parse("multipart/form-data; boundary=x");

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.InvalidFormMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Upload_ByOtherCandidateEmployerOrAdmin_ReturnsNotFound()
    {
        var setup = await _attachments.NewSetupAsync();
        var other = await _api.RegisterUserAsync("other");

        using var otherClient = _api.CreateClient(other);
        using var employerClient = _api.CreateClient(setup.Employer);
        using var adminClient = await _api.AdminClientAsync();

        foreach (var client in new[] { otherClient, employerClient, adminClient })
        {
            var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf());
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }

        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_WithoutToken_ReturnsUnauthorized()
    {
        var setup = await _attachments.NewSetupAsync();

        using var client = _api.CreateClient();
        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Upload_ToMissingApplication_ReturnsNotFound()
    {
        using var client = await _api.UserClientAsync();

        var response = await client.UploadAttachmentAsync(int.MaxValue, AttachmentTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Theory]
    [InlineData(ApplicationStatus.Rejected)]
    [InlineData(ApplicationStatus.Hired)]
    [InlineData(ApplicationStatus.Withdrawn)]
    public async Task Upload_AfterFinalDecisionOrWithdrawal_ReturnsBadRequest(ApplicationStatus status)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.SetStatusAsync(setup, status);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentScenario.StatusMessage, await response.ReadMessageAsync());
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_SecondRequest_AddsAttachmentsAndKeepsThePreviousOnes()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "first.pdf", AttachmentKind.Cv);
        var firstKey = Assert.Single(await _attachments.StoredKeysAsync(setup.Application.Id));

        var dto = await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Docx(), "second.docx", AttachmentKind.Cv);

        Assert.Equal(new[] { "first.pdf", "second.docx" }, dto.Attachments.Select(a => a.FileName));
        Assert.True(System.IO.File.Exists(_attachments.StoragePath(firstKey)));
        Assert.Equal(2, _attachments.StoredFilesOf(setup.Application.Id).Length);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    // --- limity ---

    [Fact]
    public async Task Upload_TenAttachmentsAreAllowed_TheEleventhIsRejected()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 7).Select(i => File(AttachmentTestFiles.Pdf(i), $"a{i}.pdf")).ToArray());
        var dto = await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 3).Select(i => File(AttachmentTestFiles.Png(i), $"b{i}.png")).ToArray());
        Assert.Equal(AttachmentFiles.MaxAttachments, dto.Attachments.Count);

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.Pdf(), "eleventh.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TooManyMessage, await response.ReadMessageAsync());
        Assert.Equal(AttachmentFiles.MaxAttachments, (await _attachments.RecordsAsync(setup.Application.Id)).Count);
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task Upload_ElevenFilesInOneRequest_IsRejectedAsAWhole()
    {
        var setup = await _attachments.NewSetupAsync();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentsAsync(setup.Application.Id,
            Enumerable.Range(0, AttachmentFiles.MaxAttachments + 1).Select(i => File(AttachmentTestFiles.Pdf(i), $"f{i}.pdf")).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TooManyMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_ManyMoreFilesThanTheLimit_IsRejectedAsInvalidForm()
    {
        var setup = await _attachments.NewSetupAsync();

        // części ponad limit formularza nie są nawet buforowane
        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentsAsync(setup.Application.Id,
            Enumerable.Range(0, 40).Select(i => File(AttachmentTestFiles.Pdf(i), $"f{i}.pdf")).ToArray());

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.InvalidFormMessage, await response.ReadMessageAsync());
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Upload_TotalOf25MbIsAllowed_OneMoreByteIsRejected()
    {
        var setup = await _attachments.NewSetupAsync();
        var fiveMb = AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize);
        for (var i = 0; i < 5; i++)
        {
            await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, fiveMb, $"part{i}.pdf");
        }

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, AttachmentTestFiles.PdfOfSize(AttachmentTestFiles.Pdf().Length), "tiny.pdf");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TotalTooLargeMessage, await response.ReadMessageAsync());
        var records = await _attachments.RecordsAsync(setup.Application.Id);
        Assert.Equal(AttachmentFiles.MaxTotalSize, records.Sum(r => r.Size));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task Upload_NewFilesExceeding25MbTogetherWithExistingOnes_IsRejectedAndStoresNothing()
    {
        var setup = await _attachments.NewSetupAsync();
        var fourMb = AttachmentTestFiles.PdfOfSize(4 * 1024 * 1024);
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            Enumerable.Range(0, 5).Select(i => File(fourMb, $"old{i}.pdf")).ToArray());
        var before = await _attachments.RecordsAsync(setup.Application.Id);

        // 20 MB + 2 x 3 MB = 26 MB > 25 MB, choć samo żądanie mieści się w limicie
        using var client = _api.CreateClient(setup.Candidate);
        var threeMb = AttachmentTestFiles.PdfOfSize(3 * 1024 * 1024);
        var response = await client.UploadAttachmentsAsync(setup.Application.Id, File(threeMb, "new1.pdf"), File(threeMb, "new2.pdf"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TotalTooLargeMessage, await response.ReadMessageAsync());
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    [Fact]
    public async Task Upload_OneRequestOver25MbButUnderTheRequestLimit_IsRejectedBeforeStoring()
    {
        var setup = await _attachments.NewSetupAsync();
        var uploads = Enumerable.Range(0, 5)
            .Select(i => File(AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize), $"big{i}.pdf"))
            .Append(File(AttachmentTestFiles.PdfOfSize(512 * 1024), "extra.pdf"))
            .ToArray();

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentsAsync(setup.Application.Id, uploads);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.TotalTooLargeMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    public static TheoryData<string> BadFileInTheMiddle => new() { "empty", "too large", "unsupported" };

    [Theory]
    [MemberData(nameof(BadFileInTheMiddle))]
    public async Task Upload_OneBadFileAmongGoodOnes_RejectsTheWholeRequest(string problem)
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "existing.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);
        var (bad, message) = problem switch
        {
            "empty" => (Array.Empty<byte>(), AttachmentFiles.EmptyFileMessage),
            "too large" => (AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize + 1), AttachmentFiles.TooLargeMessage),
            _ => (AttachmentTestFiles.Gif(), AttachmentFiles.UnsupportedTypeMessage)
        };

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentsAsync(setup.Application.Id,
            File(AttachmentTestFiles.Pdf(1), "good1.pdf", AttachmentKind.Cv),
            File(bad, "bad.pdf", AttachmentKind.CoverLetter),
            File(AttachmentTestFiles.Docx(), "good2.docx", AttachmentKind.Other));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(message, await response.ReadMessageAsync());
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    // --- kontrola właściciela przed odczytem treści ---

    // Liczy bajty treści żądania odczytane przez aplikację (middleware dodany przed całym potokiem aplikacji)
    private sealed class BodyCounter
    {
        private long _bytesRead;

        public long BytesRead => Interlocked.Read(ref _bytesRead);

        public void Reset() => Interlocked.Exchange(ref _bytesRead, 0);

        public void Add(int count) => Interlocked.Add(ref _bytesRead, count);
    }

    private sealed class CountingReadStream(Stream inner, BodyCounter counter) : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            var read = inner.Read(buffer, offset, count);
            counter.Add(read);
            return read;
        }

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            var read = await inner.ReadAsync(buffer, cancellationToken);
            counter.Add(read);
            return read;
        }

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
            ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed class CountingStartupFilter(BodyCounter counter) : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use(async (context, nextMiddleware) =>
            {
                context.Request.Body = new CountingReadStream(context.Request.Body, counter);
                await nextMiddleware();
            });
            next(app);
        };
    }

    [Fact]
    public async Task Upload_ByStrangerOrInLockedStatus_IsRejectedWithoutReadingTheBody()
    {
        var setup = await _attachments.NewSetupAsync();
        var (rejectedCandidate, rejectedApplication) = await _attachments.ApplyAnotherAsync(setup);
        await _api.SetApplicationStatusAsync(rejectedApplication.Id, ApplicationStatus.Rejected);
        var stranger = await _api.RegisterUserAsync("stranger");
        var payload = Enumerable.Range(0, 4)
            .Select(i => File(AttachmentTestFiles.PdfOfSize(AttachmentFiles.MaxFileSize - i), $"f{i}.pdf"))
            .ToArray();
        var bodyLength = await AttachmentTestFiles.Form(payload).ReadAsByteArrayAsync();

        var counter = new BodyCounter();
        using var factory = _api.Factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddSingleton<IStartupFilter>(new CountingStartupFilter(counter))));

        HttpClient ClientFor(TestUser user)
        {
            var client = factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });
            client.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", user.Token);
            return client;
        }

        // obcy na cudzą aplikację i właściciel aplikacji w statusie bez uploadu: odpowiedź bez czytania 20 MB
        foreach (var (user, applicationId, expected) in new[]
                 {
                     (stranger, setup.Application.Id, HttpStatusCode.NotFound),
                     (rejectedCandidate, rejectedApplication.Id, HttpStatusCode.BadRequest)
                 })
        {
            counter.Reset();
            using var client = ClientFor(user);
            var response = await client.UploadAttachmentsAsync(applicationId, payload);

            Assert.Equal(expected, response.StatusCode);
            Assert.Equal(0, counter.BytesRead);
        }

        Assert.Empty(_attachments.StoredFilesOf(rejectedApplication.Id));

        // kontrola: właściciel w dozwolonym statusie - aplikacja czyta całe ciało
        counter.Reset();
        using var ownerClient = ClientFor(setup.Candidate);
        var ownerResponse = await ownerClient.UploadAttachmentsAsync(setup.Application.Id, payload);
        Assert.Equal(HttpStatusCode.Created, ownerResponse.StatusCode);
        Assert.Equal(bodyLength.Length, counter.BytesRead);
    }

    // --- download ---

    [Fact]
    public async Task Download_ByOwnerEmployerAndAdmin_ReturnsFileWithSafeHeaders()
    {
        var setup = await _attachments.NewSetupAsync();
        var pdf = AttachmentTestFiles.Pdf();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, pdf, "Życiorys: Jan (2026).pdf");

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);
        using var admin = await _api.AdminClientAsync();

        foreach (var client in new[] { owner, employer, admin })
        {
            var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(pdf, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal("application/pdf", response.Content.Headers.ContentType?.MediaType);
            Assert.Equal(pdf.Length, response.Content.Headers.ContentLength);
            Assert.Equal("attachment; filename*=UTF-8''%C5%BByciorys_%20Jan%20%282026%29.pdf",
                string.Join(",", response.Content.Headers.GetValues("Content-Disposition")));
            Assert.Equal("Życiorys_ Jan (2026).pdf", response.Content.Headers.ContentDisposition?.FileNameStar);
            Assert.Null(response.Content.Headers.ContentDisposition?.FileName);
            Assert.Equal("nosniff", string.Join(",", response.Headers.GetValues("X-Content-Type-Options")));
            Assert.True(response.Headers.CacheControl?.NoStore);
        }
    }

    [Fact]
    public async Task Download_EachOfSeveralAttachments_ReturnsItsOwnContent()
    {
        var setup = await _attachments.NewSetupAsync();
        var files = new[]
        {
            File(AttachmentTestFiles.Pdf(1), "cv.pdf", AttachmentKind.Cv),
            File(AttachmentTestFiles.Docx(), "letter.docx", AttachmentKind.CoverLetter),
            File(AttachmentTestFiles.Jpeg(3), "photo.jpg", AttachmentKind.Portfolio)
        };
        var dto = await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id, files);

        using var employer = _api.CreateClient(setup.Employer);
        foreach (var attachment in dto.Attachments)
        {
            var response = await employer.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(files.Single(f => f.FileName == attachment.FileName).Content, await response.Content.ReadAsByteArrayAsync());
            Assert.Equal(attachment.Size, response.Content.Headers.ContentLength);
        }
    }

    [Fact]
    public async Task UploadAndDownload_FileNameWithLineBreaksAndQuotes_CannotInjectHeaders()
    {
        var setup = await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);

        var upload = await client.PostAsync(AttachmentTestFiles.Url(setup.Application.Id), AttachmentTestFiles.RawForm(AttachmentTestFiles.Pdf(),
            "filename=\"fallback.pdf\"; filename*=UTF-8''evil%22%0D%0AX-Injected%3A%201.pdf"));

        Assert.Equal(HttpStatusCode.Created, upload.StatusCode);
        var attachment = Assert.Single((await upload.ReadJsonAsync<JobApplicationDto>()).Attachments);
        Assert.Equal("evil_X-Injected_ 1.pdf", attachment.FileName);

        var download = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);

        Assert.Equal(HttpStatusCode.OK, download.StatusCode);
        Assert.False(download.Headers.Contains("X-Injected"));
        Assert.False(download.Content.Headers.Contains("X-Injected"));
        Assert.Equal("evil_X-Injected_ 1.pdf", download.Content.Headers.ContentDisposition?.FileNameStar);
    }

    [Fact]
    public async Task Upload_DocxZipBomb_ReturnsUnsupportedType()
    {
        var setup = await _attachments.NewSetupAsync();
        var bomb = AttachmentTestFiles.Zip(
            ("[Content_Types].xml", "<Types/>"),
            ("word/document.xml", new string('0', (int)AttachmentFiles.MaxDocxUncompressedSize + 1024)));

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, bomb, "bomb.docx", AttachmentTestFiles.DocxContentType);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.UnsupportedTypeMessage, await response.ReadMessageAsync());
    }

    [Fact]
    public async Task Download_ByOtherEmployerOrCandidate_ReturnsNotFound()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        var otherEmployer = await _api.RegisterApprovedEmployerAsync();
        var otherCandidate = await _api.RegisterUserAsync("other");

        using var employerClient = _api.CreateClient(otherEmployer);
        using var candidateClient = _api.CreateClient(otherCandidate);

        foreach (var client in new[] { employerClient, candidateClient })
        {
            var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await response.ReadMessageAsync());
        }
    }

    [Fact]
    public async Task Download_MissingAttachmentOrAttachmentOfAnotherApplication_ReturnsAttachmentNotFound()
    {
        var setup = await _attachments.NewSetupAsync();
        var (otherCandidate, otherApplication) = await _attachments.ApplyAnotherAsync(setup);
        var foreign = await _attachments.UploadOneAsync(otherCandidate, otherApplication.Id, AttachmentTestFiles.Pdf());

        using var owner = _api.CreateClient(setup.Candidate);
        using var employer = _api.CreateClient(setup.Employer);
        foreach (var client in new[] { owner, employer })
        {
            foreach (var attachmentId in new[] { int.MaxValue, foreign.Id })
            {
                var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachmentId);
                Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
                Assert.Equal(AttachmentScenario.AttachmentNotFound, await response.ReadMessageAsync());
            }
        }
    }

    [Fact]
    public async Task Download_WhenFileMissingInStorage_ReturnsNotFound()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());
        System.IO.File.Delete(_attachments.StoragePath(attachment.Key));

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AttachmentScenario.AttachmentNotFound, await response.ReadMessageAsync());
    }

    // --- delete ---

    [Fact]
    public async Task Delete_ByOwner_RemovesOnlyThatAttachmentAndItsFile()
    {
        var setup = await _attachments.NewSetupAsync();
        var dto = await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            File(AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv), File(AttachmentTestFiles.Docx(), "letter.docx", AttachmentKind.CoverLetter));
        var records = await _attachments.RecordsAsync(setup.Application.Id);
        var removed = records.Single(r => r.Name == "cv.pdf");
        var kept = records.Single(r => r.Name == "letter.docx");

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.DeleteAttachmentAsync(setup.Application.Id, removed.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(System.IO.File.Exists(_attachments.StoragePath(removed.Key)));
        Assert.True(System.IO.File.Exists(_attachments.StoragePath(kept.Key)));
        var application = await _api.GetApplicationAsync(client, setup.Application.Id);
        Assert.Equal("letter.docx", Assert.Single(application.Attachments).FileName);
        Assert.Equal(kept.Size, application.AttachmentsTotalSize);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DownloadAttachmentAsync(setup.Application.Id, removed.Id)).StatusCode);
        var again = await client.DeleteAttachmentAsync(setup.Application.Id, removed.Id);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal(AttachmentScenario.AttachmentNotFound, await again.ReadMessageAsync());
        Assert.Equal(2, dto.Attachments.Count);
    }

    [Fact]
    public async Task Delete_ByEmployerOrAdmin_IsNotFound_ButOwnerCanDeleteAfterRejection()
    {
        var setup = await _attachments.NewSetupAsync();
        var attachment = await _attachments.UploadOneAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf());

        using var employer = _api.CreateClient(setup.Employer);
        using var admin = await _api.AdminClientAsync();
        foreach (var client in new[] { employer, admin })
        {
            var denied = await client.DeleteAttachmentAsync(setup.Application.Id, attachment.Id);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
            Assert.Equal(AttachmentScenario.ApplicationNotFound, await denied.ReadMessageAsync());
        }

        // prawo do usunięcia danych - także po decyzji końcowej
        await _api.SetApplicationStatusAsync(setup.Application.Id, ApplicationStatus.Rejected);
        using var owner = _api.CreateClient(setup.Candidate);
        var response = await owner.DeleteAttachmentAsync(setup.Application.Id, attachment.Id);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Empty(await _attachments.RecordsAsync(setup.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(setup.Application.Id));
    }

    [Fact]
    public async Task Delete_AttachmentOfAnotherApplicationThroughOwnApplicationId_IsNotFound()
    {
        var setup = await _attachments.NewSetupAsync();
        var (otherCandidate, otherApplication) = await _attachments.ApplyAnotherAsync(setup);
        var foreign = await _attachments.UploadOneAsync(otherCandidate, otherApplication.Id, AttachmentTestFiles.Pdf());

        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.DeleteAttachmentAsync(setup.Application.Id, foreign.Id);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(AttachmentScenario.AttachmentNotFound, await response.ReadMessageAsync());
        Assert.Single(await _attachments.RecordsAsync(otherApplication.Id));
        Assert.True(System.IO.File.Exists(_attachments.StoragePath(foreign.Key)));
    }

    // --- DTO w listach, reaktywacja, sprzątanie ---

    [Fact]
    public async Task Lists_ShowAttachments()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf", AttachmentKind.Cv);

        using var owner = _api.CreateClient(setup.Candidate);
        var mine = await (await owner.GetAsync($"{Url}/my-applications")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal("cv.pdf", Assert.Single(Assert.Single(mine).Attachments).FileName);

        using var employer = _api.CreateClient(setup.Employer);
        var forJob = await (await employer.GetAsync($"{Url}/all?pageSize=50&jobOfferId={setup.Job.Id}")).ReadJsonAsync<JobApplicationDto[]>();
        Assert.Equal(AttachmentKind.Cv, Assert.Single(Assert.Single(forJob).Attachments).Kind);
    }

    [Fact]
    public async Task Reapply_AfterWithdrawal_KeepsAttachments()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        Assert.Equal(HttpStatusCode.OK, (await _api.WithdrawAsync(setup.Candidate, setup.Application.Id)).StatusCode);

        var reactivated = await _api.ApplyAsync(setup.Candidate, setup.Job.Id);

        Assert.Equal(setup.Application.Id, reactivated.Id);
        Assert.Equal("cv.pdf", Assert.Single(reactivated.Attachments).FileName);
    }

    [Fact]
    public async Task AdminDeletesApplication_RemovesAttachmentFiles()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadManyOkAsync(setup.Candidate, setup.Application.Id,
            File(AttachmentTestFiles.Pdf(), "cv.pdf"), File(AttachmentTestFiles.Png(), "photo.png"));
        var keys = await _attachments.StoredKeysAsync(setup.Application.Id);

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"{Url}/{setup.Application.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.All(keys, key => Assert.False(System.IO.File.Exists(_attachments.StoragePath(key))));
        Assert.False(await _api.QueryDbAsync(db => db.ApplicationAttachments.AnyAsync(a => a.JobApplicationId == setup.Application.Id)));
        Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"{Url}/{setup.Application.Id}")).StatusCode);
    }

    [Fact]
    public async Task AdminDeletesJob_RemovesAttachmentFilesOfItsApplications()
    {
        var setup = await _attachments.NewSetupAsync();
        var (second, secondApplication) = await _attachments.ApplyAnotherAsync(setup);
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "cv.pdf");
        await _attachments.UploadOkAsync(second, secondApplication.Id, AttachmentTestFiles.Docx(), "cv.docx");
        var keys = (await _attachments.StoredKeysAsync(setup.Application.Id)).Concat(await _attachments.StoredKeysAsync(secondApplication.Id)).ToList();

        using var admin = await _api.AdminClientAsync();
        var response = await admin.DeleteAsync($"/api/jobs/{setup.Job.Id}");

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.Equal(2, keys.Count);
        Assert.All(keys, key => Assert.False(System.IO.File.Exists(_attachments.StoragePath(key))));
        Assert.False(await _api.QueryDbAsync(db => db.JobApplications.AnyAsync(a => a.JobOfferId == setup.Job.Id)));
    }
}
