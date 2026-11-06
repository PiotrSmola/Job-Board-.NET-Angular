using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// Rozpoznawanie typu CV po zawartości i sanityzacja nazwy pliku
public class CvFilesTests
{
    private static async Task<CvFileType?> DetectAsync(byte[] content)
    {
        using var stream = new MemoryStream(content);
        var type = await CvFiles.DetectTypeAsync(stream);
        Assert.Equal(0, stream.Position);
        return type;
    }

    [Fact]
    public async Task DetectType_Pdf_ReturnsPdf()
    {
        Assert.Equal(CvFileType.Pdf, await DetectAsync(CvTestFiles.Pdf()));
    }

    [Fact]
    public async Task DetectType_Docx_ReturnsDocx()
    {
        Assert.Equal(CvFileType.Docx, await DetectAsync(CvTestFiles.Docx()));
    }

    [Fact]
    public async Task DetectType_OtherContent_ReturnsNull()
    {
        Assert.Null(await DetectAsync(CvTestFiles.Png()));
        Assert.Null(await DetectAsync("%PD"u8.ToArray()));
        Assert.Null(await DetectAsync([]));
        Assert.Null(await DetectAsync(CvTestFiles.Zip(("word/document.xml", "<w/>"))));
        Assert.Null(await DetectAsync(CvTestFiles.Zip(("[Content_Types].xml", "<Types/>"), ("ppt/slide.xml", "<p/>"))));
        Assert.Null(await DetectAsync([0x50, 0x4B, 0x03, 0x04, 0x00]));
    }

    [Fact]
    public async Task DetectType_DocxWithTooManyEntries_ReturnsNull()
    {
        var entries = Enumerable.Range(0, CvFiles.MaxDocxEntries)
            .Select(i => ($"word/media/{i}.xml", "x"))
            .Prepend(("[Content_Types].xml", "<Types/>"))
            .ToArray();

        Assert.Null(await DetectAsync(CvTestFiles.Zip(entries)));
        Assert.Equal(CvFileType.Docx, await DetectAsync(CvTestFiles.Zip(entries[..CvFiles.MaxDocxEntries])));
    }

    [Fact]
    public async Task DetectType_DocxDeclaringHugeUncompressedSize_ReturnsNullWithoutDecompressing()
    {
        // ~50 MB zer kompresuje się do kilkudziesięciu KB (zip bomb w małej skali)
        var bomb = CvTestFiles.Zip(
            ("[Content_Types].xml", "<Types/>"),
            ("word/document.xml", new string('0', (int)CvFiles.MaxDocxUncompressedSize + 1024)));
        Assert.True(bomb.Length < CvFiles.MaxFileSize);

        Assert.Null(await DetectAsync(bomb));
    }

    [Fact]
    public async Task DetectType_NonSeekableStream_Throws()
    {
        await using var stream = new BufferedStream(new NonSeekableStream());
        await Assert.ThrowsAsync<ArgumentException>(() => CvFiles.DetectTypeAsync(stream));
    }

    [Theory]
    [InlineData("cv.pdf", "cv.pdf")]
    [InlineData("CV.PDF", "CV.pdf")]
    [InlineData("C:\\Users\\jan\\Moje CV.pdf", "Moje CV.pdf")]
    [InlineData("../../etc/passwd", "passwd.pdf")]
    [InlineData("Jan.Kowalski CV", "Jan.Kowalski CV.pdf")]
    [InlineData("cv.docx", "cv.pdf")]
    [InlineData("cv\u202Efdp.exe", "cvfdp.exe.pdf")]
    [InlineData("line\r\nbreak\t.pdf", "linebreak.pdf")]
    [InlineData("a:b*c?d|e<f>g\".pdf", "a_b_c_d_e_f_g_.pdf")]
    [InlineData("   ", "cv.pdf")]
    [InlineData(null, "cv.pdf")]
    [InlineData(".pdf", "cv.pdf")]
    [InlineData("Życiorys.pdf", "Życiorys.pdf")]
    public void SanitizeFileName_ProducesSafeNameWithDetectedExtension(string? input, string expected)
    {
        Assert.Equal(expected, CvFiles.SanitizeFileName(input, CvFileType.Pdf));
    }

    [Fact]
    public void SanitizeFileName_LongName_IsTruncatedTo255WithoutSplittingSurrogates()
    {
        var name = new string('a', 249) + "😀😀😀😀" + ".docx";

        var sanitized = CvFiles.SanitizeFileName(name, CvFileType.Docx);

        Assert.True(sanitized.Length <= CvFiles.MaxFileNameLength);
        Assert.EndsWith(".docx", sanitized);
        Assert.Equal(new string('a', 249) + ".docx", sanitized);
    }

    [Fact]
    public void NewStorageKey_IsServerGenerated()
    {
        var key = CvFiles.NewStorageKey(42, CvFileType.Docx);

        Assert.Matches("^cv/42/[0-9a-f]{32}\\.docx$", key);
        Assert.NotEqual(key, CvFiles.NewStorageKey(42, CvFileType.Docx));
    }

    [Fact]
    public void ContentDisposition_EncodesNameAsUtf8()
    {
        Assert.Equal("attachment; filename*=UTF-8''Zg%C5%82oszenie%20%27a%27.pdf",
            CvFiles.ContentDisposition("Zgłoszenie 'a'.pdf"));
    }

    [Fact]
    public void ContentDisposition_WithQuotesAndLineBreaks_IsSingleSafeHeaderValue()
    {
        const string name = "evil\"\r\nX-Injected: 1;.pdf";

        var header = CvFiles.ContentDisposition(name);

        Assert.DoesNotContain('\r', header);
        Assert.DoesNotContain('\n', header);
        Assert.DoesNotContain('"', header);
        Assert.StartsWith("attachment; filename*=UTF-8''", header);
        Assert.Equal(name, Microsoft.Net.Http.Headers.ContentDispositionHeaderValue.Parse(header).FileNameStar.ToString());
    }

    [Fact]
    public void SanitizeFileName_WithQuotesAndLineBreaks_RemovesThem()
    {
        Assert.Equal("evil_X-Injected_ 1;.pdf", CvFiles.SanitizeFileName("evil\"\r\nX-Injected: 1;.pdf", CvFileType.Pdf));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
