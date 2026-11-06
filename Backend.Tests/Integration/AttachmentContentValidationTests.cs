using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Text;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Integration;

// Walidacja zawartości załączników przez API: prawdziwy DOCX, podróbki ZIP (inne formaty OOXML, brak części Word,
// uszkodzone archiwa, katalog centralny z niebezpiecznymi wartościami w EOCD), obrazy PNG / JPEG i nagłówki innych formatów
[Collection(ApiCollection.Name)]
public class AttachmentContentValidationTests
{
    private readonly ApiFixture _api;
    private readonly AttachmentScenario _attachments;

    public AttachmentContentValidationTests(ApiFixture api)
    {
        _api = api;
        _attachments = new AttachmentScenario(api);
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

    private async Task<HttpResponseMessage> UploadAsync(byte[] content, string fileName, AttachmentSetup? setup = null)
    {
        setup ??= await _attachments.NewSetupAsync();
        using var client = _api.CreateClient(setup.Candidate);
        var response = await client.UploadAttachmentAsync(setup.Application.Id, content, fileName);
        _lastSetup = setup;
        return response;
    }

    private AttachmentSetup? _lastSetup;

    private async Task AssertRejectedAsync(byte[] content, string fileName = "cv.docx")
    {
        var response = await UploadAsync(content, fileName);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AttachmentFiles.UnsupportedTypeMessage, await response.ReadMessageAsync());
        Assert.Empty(await _attachments.RecordsAsync(_lastSetup!.Application.Id));
        Assert.Empty(_attachments.StoredFilesOf(_lastSetup.Application.Id));
    }

    private async Task AssertAcceptedAsync(byte[] content, string fileName, string expectedContentType)
    {
        var response = await UploadAsync(content, fileName);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(expectedContentType, Assert.Single(await _attachments.RecordsAsync(_lastSetup!.Application.Id)).ContentType);
    }

    // --- poprawny DOCX ---

    [Fact]
    public async Task RealisticDocx_IsAcceptedStoredAndServedByteForByte()
    {
        var docx = RealisticDocx();
        var setup = await _attachments.NewSetupAsync();

        var response = await UploadAsync(docx, "Życiorys.docx", setup);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attachment = Assert.Single((await response.ReadJsonAsync<JobApplicationDto>()).Attachments);
        Assert.Equal("Życiorys.docx", attachment.FileName);
        Assert.Equal(docx.Length, attachment.Size);

        using var owner = _api.CreateClient(setup.Candidate);
        var download = await owner.DownloadAttachmentAsync(setup.Application.Id, attachment.Id);
        Assert.Equal(AttachmentTestFiles.DocxContentType, download.Content.Headers.ContentType?.MediaType);
        Assert.Equal(docx, await download.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task DocxWithStoredEntriesAndExtraParts_IsAccepted()
    {
        var docx = ZipWith(CompressionLevel.NoCompression,
            ("[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml),
            ("word/styles.xml", "<w:styles/>"), ("word/_rels/document.xml.rels", "<Relationships/>"),
            ("word/media/image1.png", "not really a png"), ("customXml/item1.xml", "<x/>"));

        await AssertAcceptedAsync(docx, "cv.docx", AttachmentTestFiles.DocxContentType);
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
        var setup = await _attachments.NewSetupAsync();

        var response = await UploadAsync(docx, "big.docx", setup);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var attachment = Assert.Single((await response.ReadJsonAsync<JobApplicationDto>()).Attachments);
        using var owner = _api.CreateClient(setup.Candidate);
        Assert.Equal(docx, await (await owner.DownloadAttachmentAsync(setup.Application.Id, attachment.Id)).Content.ReadAsByteArrayAsync());
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
        entries.AddRange(Enumerable.Range(0, AttachmentFiles.MaxDocxEntries - 1).Select(i => ($"word/media/{i}.txt", "x")));
        Assert.Equal(AttachmentFiles.MaxDocxEntries + 1, entries.Count);

        await AssertRejectedAsync(ZipWith(CompressionLevel.NoCompression, entries.ToArray()));
    }

    [Fact]
    public async Task ZipWithExactlyMaxEntries_IsAccepted()
    {
        var entries = new List<(string, string)> { ("[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml) };
        entries.AddRange(Enumerable.Range(0, AttachmentFiles.MaxDocxEntries - 2).Select(i => ($"word/media/{i}.txt", "x")));
        Assert.Equal(AttachmentFiles.MaxDocxEntries, entries.Count);

        await AssertAcceptedAsync(ZipWith(CompressionLevel.NoCompression, entries.ToArray()), "many.docx", AttachmentTestFiles.DocxContentType);
    }

    // --- rekord końca katalogu centralnego (EOCD) - sprawdzany przed otwarciem archiwum ---

    [Fact]
    public async Task CentralDirectoryLargerThan256Kb_IsRejectedEvenWithFewEntries()
    {
        // 900 wpisów (< 1000) o długich nazwach: katalog centralny ~300 KB wczytywany w całości do pamięci
        var entries = new List<(string, string)> { ("[Content_Types].xml", ContentTypesXml), ("word/document.xml", DocumentXml) };
        entries.AddRange(Enumerable.Range(0, 898).Select(i => ($"word/media/{i:D4}-{new string('n', 280)}.txt", "")));
        var docx = ZipWith(CompressionLevel.NoCompression, entries.ToArray());
        var eocd = LastIndexOf(docx, [0x50, 0x4B, 0x05, 0x06]);
        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(docx.AsSpan(eocd + 12, 4)) > AttachmentFiles.MaxDocxCentralDirectorySize);

        await AssertRejectedAsync(docx);
    }

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

    // offset w EOCD: 8 - wpisy na dysku, 10 - wpisy łącznie, 12 - rozmiar katalogu, 16 - offset katalogu, 4/6 - numery dysków
    public static TheoryData<string, int, int, uint> TamperedEndOfCentralDirectory => new()
    {
        { "ZIP64 entry count", 10, 2, 0xFFFF },
        { "ZIP64 entry count on disk", 8, 2, 0xFFFF },
        { "ZIP64 directory size", 12, 4, 0xFFFFFFFF },
        { "ZIP64 directory offset", 16, 4, 0xFFFFFFFF },
        { "directory size above 256 KB", 12, 4, 256 * 1024 + 1 },
        { "directory size smaller than its entries", 12, 4, 10 },
        { "directory size of zero", 12, 4, 0 },
        { "zero entries", 10, 2, 0 },
        { "multi-disk archive", 4, 2, 1 },
        { "directory on another disk", 6, 2, 1 }
    };

    [Theory]
    [MemberData(nameof(TamperedEndOfCentralDirectory))]
    public async Task DocxWithTamperedEndOfCentralDirectory_IsRejected(string description, int offset, int width, uint value)
    {
        var docx = RealisticDocx();
        var eocd = LastIndexOf(docx, [0x50, 0x4B, 0x05, 0x06]);
        if (width == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(eocd + offset, 2), (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(docx.AsSpan(eocd + offset, 4), value);
        }

        Assert.False(string.IsNullOrEmpty(description));
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
    public async Task DocxWithCentralDirectoryOverlappingTheEndRecord_IsRejected()
    {
        // offset + rozmiar w granicach pliku, ale katalog "zachodzi" na rekord EOCD
        var docx = RealisticDocx();
        var eocd = LastIndexOf(docx, [0x50, 0x4B, 0x05, 0x06]);
        var size = BinaryPrimitives.ReadUInt32LittleEndian(docx.AsSpan(eocd + 12, 4));
        BinaryPrimitives.WriteUInt32LittleEndian(docx.AsSpan(eocd + 16, 4), (uint)eocd - size + 10);

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
        // rekord EOCD ukryty na końcu pliku wskazuje fałszywą liczbę wpisów - plik bez części Word
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
        await AssertRejectedAsync([.. " "u8.ToArray(), .. AttachmentTestFiles.Pdf()], "cv.pdf");
        await AssertRejectedAsync([.. new byte[] { 0xEF, 0xBB, 0xBF }, .. AttachmentTestFiles.Pdf()], "cv.pdf");
    }

    [Fact]
    public async Task PdfHeaderInDifferentCase_IsRejected() =>
        await AssertRejectedAsync("%pdf-1.4\n%%EOF"u8.ToArray(), "cv.pdf");

    // --- PNG i JPEG ---

    [Fact]
    public async Task PngAndJpeg_AreAcceptedByTheirSignatures()
    {
        await AssertAcceptedAsync(AttachmentTestFiles.Png(), "image.png", AttachmentTestFiles.PngContentType);
        await AssertAcceptedAsync(AttachmentTestFiles.Jpeg(), "image.jpg", AttachmentTestFiles.JpegContentType);
        // JPEG z innym znacznikiem APP (np. EXIF z aparatu) - liczą się tylko pierwsze trzy bajty
        await AssertAcceptedAsync([0xFF, 0xD8, 0xFF, 0xE1, 0x00, 0x10, 0x45, 0x78, 0x69, 0x66], "camera.jpg", AttachmentTestFiles.JpegContentType);
    }

    [Fact]
    public async Task IncompleteOrDamagedImageSignatures_AreRejected()
    {
        var samples = new Dictionary<string, byte[]>
        {
            // 7 z 8 bajtów sygnatury PNG
            ["short.png"] = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A],
            // sygnatura PNG uszkodzona przez transfer w trybie tekstowym (CRLF -> LF)
            ["text-mode.png"] = [0x89, 0x50, 0x4E, 0x47, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00],
            ["lowercase.png"] = [0x89, 0x70, 0x6E, 0x67, 0x0D, 0x0A, 0x1A, 0x0A, 0x00],
            ["short.jpg"] = [0xFF, 0xD8],
            ["no-soi.jpg"] = [0xFF, 0xD9, 0xFF, 0xE0, 0x00]
        };

        foreach (var (name, content) in samples)
        {
            await AssertRejectedAsync(content, name);
        }
    }

    [Fact]
    public async Task OtherFormats_AreRejectedByContent()
    {
        var samples = new Dictionary<string, byte[]>
        {
            ["gif.pdf"] = AttachmentTestFiles.Gif(),
            ["bmp.png"] = "BM\x36\x00\x0C\x00\x00\x00\x00\x00"u8.ToArray(),
            ["webp.jpg"] = "RIFF\x24\x00\x00\x00WEBPVP8 "u8.ToArray(),
            ["tiff.jpg"] = [0x49, 0x49, 0x2A, 0x00, 0x08, 0x00, 0x00, 0x00],
            ["exe.pdf"] = "MZ\x90\x00\x03\x00\x00\x00"u8.ToArray(),
            ["html.pdf"] = "<!DOCTYPE html><html><script>alert(1)</script></html>"u8.ToArray(),
            ["svg.png"] = "<svg xmlns=\"http://www.w3.org/2000/svg\"><script>alert(1)</script></svg>"u8.ToArray(),
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
    public async Task RejectedUpload_DoesNotTouchTheExistingAttachments()
    {
        var setup = await _attachments.NewSetupAsync();
        await _attachments.UploadOkAsync(setup.Candidate, setup.Application.Id, AttachmentTestFiles.Pdf(), "good.pdf");
        var before = await _attachments.RecordsAsync(setup.Application.Id);

        var response = await UploadAsync(AttachmentTestFiles.Gif(), "bad.png", setup);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(before, await _attachments.RecordsAsync(setup.Application.Id));
        await _attachments.AssertStorageMatchesRecordsAsync(setup.Application.Id);
    }

    private static int IndexOf(byte[] data, byte[] pattern) => data.AsSpan().IndexOf(pattern);

    private static int LastIndexOf(byte[] data, byte[] pattern) => data.AsSpan().LastIndexOf(pattern);
}
