using System.Buffers.Binary;
using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// Rozpoznawanie typu załącznika po zawartości, kontrola katalogu centralnego DOCX i sanityzacja nazwy pliku
public class AttachmentFilesTests
{
    private static async Task<AttachmentFileType?> DetectAsync(byte[] content)
    {
        using var stream = new MemoryStream(content);
        var type = await AttachmentFiles.DetectTypeAsync(stream);
        Assert.Equal(0, stream.Position);
        return type;
    }

    [Fact]
    public async Task DetectType_SupportedFormats_AreRecognized()
    {
        Assert.Equal(AttachmentFileType.Pdf, await DetectAsync(AttachmentTestFiles.Pdf()));
        Assert.Equal(AttachmentFileType.Docx, await DetectAsync(AttachmentTestFiles.Docx()));
        Assert.Equal(AttachmentFileType.Png, await DetectAsync(AttachmentTestFiles.Png()));
        Assert.Equal(AttachmentFileType.Jpeg, await DetectAsync(AttachmentTestFiles.Jpeg()));
        // sama sygnatura wystarcza (PNG 8 B, JPEG 3 B)
        Assert.Equal(AttachmentFileType.Png, await DetectAsync([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]));
        Assert.Equal(AttachmentFileType.Jpeg, await DetectAsync([0xFF, 0xD8, 0xFF]));
    }

    [Fact]
    public void FileTypes_HaveServerSideExtensionsAndContentTypes()
    {
        Assert.Equal((".pdf", "application/pdf"), (AttachmentFileType.Pdf.Extension, AttachmentFileType.Pdf.ContentType));
        Assert.Equal((".docx", AttachmentTestFiles.DocxContentType), (AttachmentFileType.Docx.Extension, AttachmentFileType.Docx.ContentType));
        Assert.Equal((".png", "image/png"), (AttachmentFileType.Png.Extension, AttachmentFileType.Png.ContentType));
        Assert.Equal((".jpg", "image/jpeg"), (AttachmentFileType.Jpeg.Extension, AttachmentFileType.Jpeg.ContentType));
    }

    [Fact]
    public async Task DetectType_OtherContent_ReturnsNull()
    {
        Assert.Null(await DetectAsync(AttachmentTestFiles.Gif()));
        Assert.Null(await DetectAsync("%PD"u8.ToArray()));
        Assert.Null(await DetectAsync([]));
        Assert.Null(await DetectAsync([0x89, 0x50, 0x4E, 0x47]));
        Assert.Null(await DetectAsync([0xFF, 0xD8]));
        Assert.Null(await DetectAsync(AttachmentTestFiles.Zip(("word/document.xml", "<w/>"))));
        Assert.Null(await DetectAsync(AttachmentTestFiles.Zip(("[Content_Types].xml", "<Types/>"), ("ppt/slide.xml", "<p/>"))));
        Assert.Null(await DetectAsync([0x50, 0x4B, 0x03, 0x04, 0x00]));
    }

    [Fact]
    public async Task DetectType_DocxWithTooManyEntries_ReturnsNull()
    {
        var entries = Enumerable.Range(0, AttachmentFiles.MaxDocxEntries)
            .Select(i => ($"word/media/{i}.xml", "x"))
            .Prepend(("[Content_Types].xml", "<Types/>"))
            .ToArray();

        Assert.Null(await DetectAsync(AttachmentTestFiles.Zip(entries)));
        Assert.Equal(AttachmentFileType.Docx, await DetectAsync(AttachmentTestFiles.Zip(entries[..AttachmentFiles.MaxDocxEntries])));
    }

    [Fact]
    public async Task DetectType_DocxDeclaringHugeUncompressedSize_ReturnsNullWithoutDecompressing()
    {
        // ~50 MB zer kompresuje się do kilkudziesięciu KB (zip bomb w małej skali)
        var bomb = AttachmentTestFiles.Zip(
            ("[Content_Types].xml", "<Types/>"),
            ("word/document.xml", new string('0', (int)AttachmentFiles.MaxDocxUncompressedSize + 1024)));
        Assert.True(bomb.Length < AttachmentFiles.MaxFileSize);

        Assert.Null(await DetectAsync(bomb));
    }

    [Theory]
    [InlineData(10, 2, 0xFFFFL)] // ZIP64 - liczba wpisów
    [InlineData(12, 4, 0xFFFFFFFFL)] // ZIP64 - rozmiar katalogu
    [InlineData(16, 4, 0xFFFFFFFFL)] // ZIP64 - offset katalogu
    [InlineData(12, 4, 256L * 1024 + 1)] // katalog większy niż 256 KB
    [InlineData(16, 4, 0x7FFFFFFFL)] // offset poza plikiem
    [InlineData(12, 4, 1L)] // rozmiar mniejszy niż minimalny dla zadeklarowanych wpisów
    [InlineData(4, 2, 1L)] // archiwum wieloczęściowe
    public async Task DetectType_DocxWithDangerousEndOfCentralDirectory_ReturnsNull(int offset, int width, long value)
    {
        var docx = AttachmentTestFiles.Docx();
        var eocd = docx.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        if (width == 2)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(docx.AsSpan(eocd + offset, 2), (ushort)value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32LittleEndian(docx.AsSpan(eocd + offset, 4), (uint)value);
        }

        Assert.Null(await DetectAsync(docx));
    }

    // katalog centralny musi kończyć się dokładnie na rekordzie EOCD - dane wstawione pomiędzy (ukryta treść, której
    // nie opisuje żaden wpis) są odrzucane, choć ZipArchive by je pominął
    [Theory]
    [InlineData(1)]
    [InlineData(64)]
    public async Task DetectType_DocxWithBytesBetweenCentralDirectoryAndEndRecord_ReturnsNull(int gap)
    {
        var docx = AttachmentTestFiles.Docx();
        var eocd = docx.AsSpan().LastIndexOf(new byte[] { 0x50, 0x4B, 0x05, 0x06 });
        Assert.NotNull(await DetectAsync(docx));

        byte[] withGap = [.. docx.AsSpan(0, eocd), .. new byte[gap], .. docx.AsSpan(eocd)];

        Assert.Null(await DetectAsync(withGap));
    }

    [Fact]
    public async Task DetectType_NonSeekableStream_Throws()
    {
        await using var stream = new BufferedStream(new NonSeekableStream());
        await Assert.ThrowsAsync<ArgumentException>(() => AttachmentFiles.DetectTypeAsync(stream));
    }

    [Theory]
    [InlineData("cv.pdf", "cv.pdf")]
    [InlineData("CV.PDF", "CV.pdf")]
    [InlineData("C:\\Users\\jan\\Moje CV.pdf", "Moje CV.pdf")]
    [InlineData("../../etc/passwd", "passwd.pdf")]
    [InlineData("Jan.Kowalski CV", "Jan.Kowalski CV.pdf")]
    [InlineData("cv.docx", "cv.pdf")]
    [InlineData("scan.jpeg", "scan.pdf")]
    [InlineData("cv\u202Efdp.exe", "cvfdp.exe.pdf")]
    [InlineData("line\r\nbreak\t.pdf", "linebreak.pdf")]
    [InlineData("a:b*c?d|e<f>g\".pdf", "a_b_c_d_e_f_g_.pdf")]
    [InlineData("   ", "attachment.pdf")]
    [InlineData(null, "attachment.pdf")]
    [InlineData(".pdf", "attachment.pdf")]
    [InlineData(". . .", "attachment.pdf")]
    [InlineData("a . . .", "a.pdf")]
    [InlineData("a .\u0001.", "a.pdf")]
    [InlineData("Życiorys.pdf", "Życiorys.pdf")]
    [InlineData("CON", "_CON.pdf")]
    [InlineData("con.pdf", "_con.pdf")]
    [InlineData("AUX.backup.pdf", "_AUX.backup.pdf")]
    [InlineData("nul .pdf", "_nul.pdf")]
    [InlineData("COM9.pdf", "_COM9.pdf")]
    [InlineData("LPT1", "_LPT1.pdf")]
    [InlineData("COM0.pdf", "COM0.pdf")]
    [InlineData("CONFIG.pdf", "CONFIG.pdf")]
    [InlineData("my CON.pdf", "my CON.pdf")]
    public void SanitizeFileName_ProducesSafeNameWithDetectedExtension(string? input, string expected)
    {
        Assert.Equal(expected, AttachmentFiles.SanitizeFileName(input, AttachmentFileType.Pdf));
    }

    [Theory]
    [InlineData("photo.png", "photo.jpg")]
    [InlineData("photo.JPEG", "photo.jpg")]
    [InlineData("photo", "photo.jpg")]
    public void SanitizeFileName_ImageGetsTheDetectedExtension(string input, string expected)
    {
        Assert.Equal(expected, AttachmentFiles.SanitizeFileName(input, AttachmentFileType.Jpeg));
    }

    [Fact]
    public void SanitizeFileName_LongName_IsTruncatedTo255WithoutSplittingSurrogates()
    {
        var name = new string('a', 249) + "😀😀😀😀" + ".docx";

        var sanitized = AttachmentFiles.SanitizeFileName(name, AttachmentFileType.Docx);

        Assert.True(sanitized.Length <= AttachmentFiles.MaxFileNameLength);
        Assert.EndsWith(".docx", sanitized);
        Assert.Equal(new string('a', 249) + ".docx", sanitized);
    }

    [Fact]
    public void SanitizeFileName_TruncationDoesNotLeaveTrailingDotsOrSpaces()
    {
        var name = new string('a', 248) + " . ." + new string('b', 20) + ".pdf";

        var sanitized = AttachmentFiles.SanitizeFileName(name, AttachmentFileType.Pdf);

        Assert.Equal(new string('a', 248) + ".pdf", sanitized);
    }

    [Fact]
    public void SanitizeFileName_ReservedNameAtMaxLength_StaysWithinTheLimit()
    {
        var sanitized = AttachmentFiles.SanitizeFileName("CON." + new string('x', 300), AttachmentFileType.Pdf);

        Assert.StartsWith("_CON.", sanitized);
        Assert.Equal(AttachmentFiles.MaxFileNameLength, sanitized.Length);
        Assert.EndsWith(".pdf", sanitized);
    }

    [Fact]
    public void NewStorageKey_IsServerGenerated()
    {
        var key = AttachmentFiles.NewStorageKey(42, AttachmentFileType.Docx);

        Assert.Matches("^attachments/42/[0-9a-f]{32}\\.docx$", key);
        Assert.NotEqual(key, AttachmentFiles.NewStorageKey(42, AttachmentFileType.Docx));
        Assert.StartsWith(AttachmentFiles.StorageKeyPrefix, key);
        Assert.Matches("^attachments/7/[0-9a-f]{32}\\.jpg$", AttachmentFiles.NewStorageKey(7, AttachmentFileType.Jpeg));
    }

    [Fact]
    public void Limits_MatchTheContract()
    {
        Assert.Equal(5_242_880, AttachmentFiles.MaxFileSize);
        Assert.Equal(26_214_400, AttachmentFiles.MaxTotalSize);
        Assert.Equal(10, AttachmentFiles.MaxAttachments);
        Assert.Equal(27_262_976, AttachmentFiles.MaxRequestSize);
        Assert.Equal(256 * 1024, AttachmentFiles.MaxDocxCentralDirectorySize);
    }

    [Fact]
    public void ContentDisposition_EncodesNameAsUtf8()
    {
        Assert.Equal("attachment; filename*=UTF-8''Zg%C5%82oszenie%20%27a%27.pdf",
            AttachmentFiles.ContentDisposition("Zgłoszenie 'a'.pdf"));
    }

    [Fact]
    public void ContentDisposition_WithQuotesAndLineBreaks_IsSingleSafeHeaderValue()
    {
        const string name = "evil\"\r\nX-Injected: 1;.pdf";

        var header = AttachmentFiles.ContentDisposition(name);

        Assert.DoesNotContain('\r', header);
        Assert.DoesNotContain('\n', header);
        Assert.DoesNotContain('"', header);
        Assert.StartsWith("attachment; filename*=UTF-8''", header);
        Assert.Equal(name, Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.Parse(header).FileNameStar.ToString());
    }

    [Fact]
    public void SanitizeFileName_WithQuotesAndLineBreaks_RemovesThem()
    {
        Assert.Equal("evil_X-Injected_ 1;.pdf", AttachmentFiles.SanitizeFileName("evil\"\r\nX-Injected: 1;.pdf", AttachmentFileType.Pdf));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
