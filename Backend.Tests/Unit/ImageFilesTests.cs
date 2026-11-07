using Backend.Helpers;
using Backend.Tests.Infrastructure;

namespace Backend.Tests.Unit;

// Walidacja obrazów firm po zawartości: tylko PNG i JPEG, wymiary z nagłówka (IHDR / SOFn), odrzucenie uszkodzonych
// nagłówków i absurdalnych wymiarów
public class ImageFilesTests
{
    private static Task<ImageFileInfo?> ReadAsync(byte[] content) => ImageFiles.ReadInfoAsync(new MemoryStream(content));

    private static Task<ImageValidationResult> ValidateAsync(byte[] content) =>
        ImageFiles.ValidateAsync(new MemoryStream(content), CancellationToken.None);

    [Fact]
    public async Task Png_ReadsDimensionsFromIhdr()
    {
        var image = await ReadAsync(CompanyMediaTestFiles.Png(1920, 1080));

        Assert.NotNull(image);
        Assert.Equal(AttachmentFileType.Png, image.Type);
        Assert.Equal(1920, image.Width);
        Assert.Equal(1080, image.Height);
    }

    [Theory]
    [InlineData(false, 0, false)]
    [InlineData(true, 0, false)]
    [InlineData(false, 3000, false)]
    // najdłuższy możliwy segment (65535 B z długością)
    [InlineData(true, 65533, true)]
    public async Task Jpeg_ReadsDimensionsFromFirstSofSegment(bool progressive, int app1Bytes, bool fillBytes)
    {
        var image = await ReadAsync(CompanyMediaTestFiles.Jpeg(1200, 900, progressive, app1Bytes, fillBytes));

        Assert.NotNull(image);
        Assert.Equal(AttachmentFileType.Jpeg, image.Type);
        Assert.Equal(1200, image.Width);
        Assert.Equal(900, image.Height);
    }

    [Fact]
    public async Task ReadInfo_RestoresStreamPosition()
    {
        var stream = new MemoryStream(CompanyMediaTestFiles.Jpeg(app1Bytes: 500));

        await ImageFiles.ReadInfoAsync(stream);

        Assert.Equal(0, stream.Position);
    }

    public static TheoryData<string, byte[]> UnsupportedFiles => new()
    {
        { "gif", AttachmentTestFiles.Gif() },
        { "pdf", AttachmentTestFiles.Pdf() },
        { "docx", AttachmentTestFiles.Docx() },
        { "text", "hello world, not an image"u8.ToArray() },
        // sama sygnatura PNG / początek IHDR bez wymiarów
        { "png-truncated", AttachmentTestFiles.Png() },
        // pierwszy chunk inny niż IHDR
        { "png-no-ihdr", [.. CompanyMediaTestFiles.Png()[..12], .. "IDAT"u8.ToArray(), .. new byte[12]] },
        // wymiar 0
        { "png-zero-width", CompanyMediaTestFiles.Png(0, 100) },
        // szerokość > 2^31 - 1
        { "png-huge", [.. CompanyMediaTestFiles.Png()[..16], 0x80, 0, 0, 0, .. CompanyMediaTestFiles.Png()[20..]] },
        // JPEG: sam SOI + APP0 (bez SOFn)
        { "jpeg-no-sof", AttachmentTestFiles.Jpeg() },
        // JPEG: dane obrazu (SOS) przed SOFn
        { "jpeg-sos-first", [0xFF, 0xD8, 0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0xFF, 0xD9] },
        // JPEG: segment dłuższy niż plik
        { "jpeg-truncated-segment", [0xFF, 0xD8, 0xFF, 0xE1, 0x40, 0x00, 0x01, 0x02] },
        // JPEG: wysokość 0 (DNL)
        { "jpeg-zero-height", CompanyMediaTestFiles.Jpeg(100, 0) },
        // JPEG: śmieci zamiast znacznika
        { "jpeg-garbage", [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x04, 0x00, 0x00, 0x12, 0x34, 0x56] },
        { "empty", [] }
    };

    [Theory]
    [MemberData(nameof(UnsupportedFiles))]
    public async Task UnsupportedOrDamagedFiles_AreRejected(string name, byte[] content)
    {
        Assert.Null(await ReadAsync(content));

        var result = await ValidateAsync(content);
        Assert.Null(result.Image);
        Assert.True(result.Error == ImageFiles.UnsupportedTypeMessage, name);
    }

    [Theory]
    [InlineData(12001, 100)]
    [InlineData(100, 12001)]
    [InlineData(8000, 6000)]
    public async Task AbsurdDimensions_AreRejected(int width, int height)
    {
        var png = await ValidateAsync(CompanyMediaTestFiles.Png(width, height));
        var jpeg = await ValidateAsync(CompanyMediaTestFiles.Jpeg(width, height));

        Assert.Equal(ImageFiles.DimensionsMessage, png.Error);
        Assert.Equal(ImageFiles.DimensionsMessage, jpeg.Error);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(12000, 3000)]
    [InlineData(8000, 5000)]
    public async Task DimensionsWithinLimits_AreAccepted(int width, int height)
    {
        var result = await ValidateAsync(CompanyMediaTestFiles.Png(width, height));

        Assert.Null(result.Error);
        Assert.Equal(width, result.Image!.Width);
        Assert.Equal(height, result.Image.Height);
    }

    [Fact]
    public async Task ReadInfo_RequiresSeekableStream()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => ImageFiles.ReadInfoAsync(new NonSeekableStream()));
    }

    private sealed class NonSeekableStream : MemoryStream
    {
        public override bool CanSeek => false;
    }
}
