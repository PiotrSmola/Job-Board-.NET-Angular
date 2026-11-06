using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Walidacja zawartości CV przez API: prawdziwy DOCX, podróbki ZIP (inne formaty OOXML, brak części Word,
// uszkodzone archiwa) i nagłówki innych formatów
[Collection(ApiCollection.Name)]
public class CvDocxValidationTests
{
    private const string Url = "/api/jobapplications";

    private readonly ApiFixture _api;
    private readonly CvScenario _cv;

    public CvDocxValidationTests(ApiFixture api)
    {
        _api = api;
        _cv = new CvScenario(api);
    }

    private const string ContentTypesXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">" +
        "<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>" +
        "<Default Extension=\"xml\" ContentType=\"application/xml\"/>" +
        "<Override PartName=\"/word/document.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml\"/>" +
        "</Types>";

    private const string RootRelsXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" +
        "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"word/document.xml\"/>" +
        "</Relationships>";

    private const string DocumentXml =
        "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
        "<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>" +
        "<w:p><w:r><w:t>Jan Kowalski - Życiorys</w:t></w:r></w:p></w:body></w:document>";

    // Zip jak z Worda: kompresja Deflate, ułożenie [Content_Types].xml, _rels/.rels, word/document.xml
    private static byte[] RealisticDocx() => ZipWith(CompressionLevel.Optimal,
        ("[Content_Types].xml", ContentTypesXml), ("_rels/.rels", RootRelsXml), ("word/document.xml", DocumentXml),
        ("docProps/app.xml", "<Properties/>"));

    private static byte[] ZipWith(CompressionLevel level, params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name, level).Open(), new UTF8Encoding(false));
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    private async Task<HttpResponseMessage> UploadAsync(byte[] content, string fileName, CvSetup? setup = null)
    {
        setup ??= await _cv.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadCvAsync(setup.Application.Id, content, fileName);
        _lastSetup = setup;
        return response;
    }

    private CvSetup? _lastSetup;

    private async Task AssertRejectedAsync(byte[] content, string fileName = "cv.docx")
    {
        var response = await UploadAsync(content, fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(CvFiles.UnsupportedTypeMessage, await response.ReadMessageAsync());
        Assert.Null(await _cv.StoredKeyAsync(_lastSetup!.Application.Id));
        Assert.Empty(_cv.StoredFilesOf(_lastSetup.Application.Id));
    }

    // --- poprawny DOCX ---

    [Fact]
    public async Task RealisticDocx_IsAcceptedStoredAndServedByteForByte()
    {
        var docx = RealisticDocx();
        var setup = await _cv.NewSetupAsync();

        var response = await UploadAsync(docx, "Życiorys.docx", setup);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var dto = await response.ReadJsonAsync<JobApplicationDto>();
        Assert.Equal("Życiorys.docx", dto.CvFileName);
        Assert.Equal(docx.Length, dto.CvSize);

        using var owner = _api.CreateClient(setup.Candidate);
        var download = await owner.GetAsync($"{Url}/{setup.Application.Id}/cv");
        Assert.Equal(CvTestFiles.DocxContentType, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(docx, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DocxWithStoredEntriesAndExtraParts_IsAccepted()
    {
        var docx = ZipWith(CompressionLevel.NoCompression,
            ("[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml),
            ("word/styles.xml", "<w:styles/>"), ("word/_rels/document.xml.rels", "<Relationships/>"),
            ("word/media/image1.png", "not really a png"), ("customXml/item1.xml", "<x/>"));

        var response = await UploadAsync(docx, "cv.docx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task DocxLargerThanOneHttpBuffer_IsAccepted()
    {
        // plik > 64 KB jest buforowany na dysku przez ASP.NET Core - strumień musi dać się przewinąć do katalogu centralnego
        var random = new Random(42);
        var payload = new byte[300 * 1024];
        random.NextBytes(payload);
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using (var writer = new StreamWriter(archive.CreateEntry("[Content_Types].xml").Open())) writer.Write(ContentTypesXml);
            using (var writer = new StreamWriter(archive.CreateEntry("word/document.xml").Open())) writer.Write(DocumentXml);
            using var media = archive.CreateEntry("word/media/image1.bin", CompressionLevel.NoCompression).Open();
            media.Write(payload);
        }

        var docx = buffer.ToArray();
        Assert.True(docx.Length > 300 * 1024);
        var setup = await _cv.NewSetupAsync();

        var response = await UploadAsync(docx, "big.docx", setup);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var owner = _api.CreateClient(setup.Candidate);
        Assert.Equal(docx, await (await owner.GetAsync($"{Url}/{setup.Application.Id}/cv")).Content.ReadAsByteArrayAsync());
    }

    // --- ZIP, który nie jest dokumentem Word ---

    [Fact]
    public async Task ZipWithoutWordPart_IsRejected() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal, ("[Content_Types].xml", ContentTypesXml), ("docProps/app.xml", "<Properties/>")));

    [Fact]
    public async Task ZipWithoutAnyContentTypes_IsRejected() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal, ("readme.txt", "hello"), ("src/Program.cs", "class P {}")));

    [Fact]
    public async Task ZipWithWordPartButWithoutContentTypes_IsRejected() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal, ("word/document.xml", DocumentXml)));

    [Fact]
    public async Task ContentTypesNestedInAFolder_IsRejected() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal, ("sub/[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml)));

    [Fact]
    public async Task WordFolderNestedInAnotherFolder_IsRejected() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal, ("[Content_Types].xml", ContentTypesXml), ("backup/word/document.xml", DocumentXml)));

    [Fact]
    public async Task Xlsx_IsRejectedEvenWithDocxName() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal,
            ("[Content_Types].xml", ContentTypesXml.Replace("/word/document.xml", "/xl/workbook.xml")),
            ("_rels/.rels", RootRelsXml.Replace("word/document.xml", "xl/workbook.xml")),
            ("xl/workbook.xml", "<workbook/>"), ("xl/worksheets/sheet1.xml", "<worksheet/>")), "salary.docx");

    [Fact]
    public async Task Pptx_IsRejectedEvenWithDocxName() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal,
            ("[Content_Types].xml", ContentTypesXml.Replace("/word/document.xml", "/ppt/presentation.xml")),
            ("ppt/presentation.xml", "<p:presentation/>")), "slides.docx");

    [Fact]
    public async Task JarOrApk_IsRejected() =>
        await AssertRejectedAsync(ZipWith(CompressionLevel.Optimal,
            ("META-INF/MANIFEST.MF", "Manifest-Version: 1.0"), ("Evil.class", "CAFEBABE")), "cv.docx");

    [Fact]
    public async Task EmptyZip_IsRejected()
    {
        using var buffer = new MemoryStream();
        using (new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
        }

        // pusty ZIP to sam rekord końca katalogu (PK 05 06) - nie zaczyna się od sygnatury lokalnego nagłówka
        await AssertRejectedAsync(buffer.ToArray());
    }

    [Fact]
    public async Task ZipWithTooManyEntries_IsRejected()
    {
        var entries = new List<(string, string)> { ("[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml) };
        entries.AddRange(Enumerable.Range(0, CvFiles.MaxDocxEntries - 1).Select(i => ($"word/media/{i}.txt", "x")));
        Assert.Equal(CvFiles.MaxDocxEntries + 1, entries.Count);

        await AssertRejectedAsync(ZipWith(CompressionLevel.NoCompression, entries.ToArray()));
    }

    [Fact]
    public async Task ZipWithExactlyMaxEntries_IsAccepted()
    {
        var entries = new List<(string, string)> { ("[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml) };
        entries.AddRange(Enumerable.Range(0, CvFiles.MaxDocxEntries - 2).Select(i => ($"word/media/{i}.txt", "x")));
        Assert.Equal(CvFiles.MaxDocxEntries, entries.Count);

        var response = await UploadAsync(ZipWith(CompressionLevel.NoCompression, entries.ToArray()), "many.docx");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // --- uszkodzone archiwa ---

    [Fact]
    public async Task TruncatedDocx_WithoutEndOfCentralDirectory_IsRejected()
    {
        var docx = RealisticDocx();

        await AssertRejectedAsync(docx[..^40]);
    }

    [Fact]
    public async Task DocxCutInHalf_IsRejected()
    {
        var docx = RealisticDocx();

        await AssertRejectedAsync(docx[..(docx.Length / 2)]);
    }

    [Fact]
    public async Task DocxWithDamagedCentralDirectory_IsRejected()
    {
        var docx = RealisticDocx();
        var central = IndexOf(docx, [0x50, 0x4B, 0x01, 0x02]);
        Assert.True(central > 0);
        docx[central + 2] = 0x00; // psuje sygnaturę pierwszego wpisu katalogu centralnego

        await AssertRejectedAsync(docx);
    }

    [Fact]
    public async Task DocxWithEntryCountLargerThanDirectory_IsRejected()
    {
        var docx = RealisticDocx();
        var eocd = LastIndexOf(docx, [0x50, 0x4B, 0x05, 0x06]);
        Assert.True(eocd > 0);
        // liczba wpisów (na dysku i łącznie) większa niż faktycznie zapisane
        BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(eocd + 8, 2), 200);
        BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(eocd + 10, 2), 200);

        await AssertRejectedAsync(docx);
    }

    [Fact]
    public async Task DocxDeclaringZip64EntryCount_IsRejected()
    {
        var docx = RealisticDocx();
        var eocd = LastIndexOf(docx, [0x50, 0x4B, 0x05, 0x06]);
        BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(eocd + 10, 2), 0xFFFF);

        await AssertRejectedAsync(docx);
    }

    [Fact]
    public async Task DocxWithCentralDirectoryOffsetBeyondTheFile_IsRejected()
    {
        var docx = RealisticDocx();
        var eocd = LastIndexOf(docx, [0x50, 0x4B, 0x05, 0x06]);
        BinaryPrimitives.WriteUInt32LittleEndian(docx.AsSpan(eocd + 16, 4), (uint)docx.Length + 1000);

        await AssertRejectedAsync(docx);
    }

    [Fact]
    public async Task ZipSignatureFollowedByGarbage_IsRejected()
    {
        var garbage = new byte[2048];
        new Random(7).NextBytes(garbage);
        garbage[0] = 0x50;
        garbage[1] = 0x4B;
        garbage[2] = 0x03;
        garbage[3] = 0x04;

        await AssertRejectedAsync(garbage);
    }

    [Fact]
    public async Task OnlyTheLocalHeaderSignature_IsRejected() =>
        await AssertRejectedAsync([0x50, 0x4B, 0x03, 0x04]);

    [Fact]
    public async Task ZipFollowedByAnotherEndOfCentralDirectoryInTrailingComment_DoesNotConfuseTheReader()
    {
        // rekord EOCD ukryty w komentarzu na końcu pliku wskazuje fałszywą liczbę wpisów - plik bez części Word
        var zip = ZipWith(CompressionLevel.Optimal, ("readme.txt", "hello"));
        var fakeEocd = new byte[22];
        fakeEocd[0] = 0x50; fakeEocd[1] = 0x4B; fakeEocd[2] = 0x05; fakeEocd[3] = 0x06;
        BinaryPrimitives.WriteUInt16LittleEndian(fakeEocd.AsSpan(10, 2), 2);

        await AssertRejectedAsync([.. zip, .. fakeEocd]);
    }

    [Fact]
    public async Task OleCompoundDocument_LegacyDocOrEncryptedDocx_IsRejected()
    {
        var ole = new byte[4096];
        byte[] signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];
        signature.CopyTo(ole, 0);

        await AssertRejectedAsync(ole, "legacy.doc");
        await AssertRejectedAsync(ole, "encrypted.docx");
    }

    // --- PDF ---

    [Fact]
    public async Task PdfWithLeadingWhitespaceOrBom_IsRejected()
    {
        await AssertRejectedAsync([.. " "u8.ToArray(), .. CvTestFiles.Pdf()], "cv.pdf");
        await AssertRejectedAsync([.. new byte[] { 0xEF, 0xBB, 0xBF }, .. CvTestFiles.Pdf()], "cv.pdf");
    }

    [Fact]
    public async Task PdfHeaderInDifferentCase_IsRejected() =>
        await AssertRejectedAsync("%pdf-1.4\n%%EOF"u8.ToArray(), "cv.pdf");

    [Fact]
    public async Task OtherFormats_AreRejectedByContent()
    {
        var samples = new Dictionary<string, byte[]>
        {
            ["gif.pdf"] = "GIF89a\x01\x00\x01\x00"u8.ToArray(),
            ["jpg.pdf"] = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46],
            ["exe.pdf"] = "MZ\x90\x00\x03\x00\x00\x00"u8.ToArray(),
            ["html.pdf"] = "<!DOCTYPE html><html><script>alert(1)</script></html>"u8.ToArray(),
            ["svg.pdf"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
            ["rtf.docx"] = "{\\rtf1\\ansi Hello}"u8.ToArray(),
            ["gzip.docx"] = [0x1F, 0x8B, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00],
            ["short.pdf"] = "%PDF"u8.ToArray(),
            ["one.pdf"] = [0x25]
        };

        foreach (var (name, content) in samples)
        {
            await AssertRejectedAsync(content, name);
        }
    }

    [Fact]
    public async Task RejectedUpload_DoesNotTouchTheExistingCv()
    {
        var setup = await _cv.NewSetupAsync();
        await _cv.UploadOkAsync(setup.Candidate, setup.Application.Id, CvTestFiles.Pdf(), "good.pdf");
        var before = await _cv.CvRecordAsync(setup.Application.Id);

        var response = await UploadAsync(CvTestFiles.Png(), "bad.pdf", setup);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await _cv.CvRecordAsync(setup.Application.Id));
        Assert.True(File.Exists(_cv.StoragePath(before.Key!)));
        Assert.Single(_cv.StoredFilesOf(setup.Application.Id));
    }

    private static int IndexOf(byte[] data, byte[] pattern) => data.AsSpan().IndexOf(pattern);

    private static int LastIndexOf(byte[] data, byte[] pattern) => data.AsSpan().LastIndexOf(pattern);
}
