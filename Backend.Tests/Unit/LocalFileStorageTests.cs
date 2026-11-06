using System.Text;
using Backend.Services;

namespace Backend.Tests.Unit;

// Magazyn plików na dysku (katalog tymczasowy usuwany po każdym teście)
public sealed class LocalFileStorageTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "jobboard-local-storage-" + Guid.NewGuid().ToString("N"));
    private readonly LocalFileStorage _storage;

    public LocalFileStorageTests()
    {
        _storage = new LocalFileStorage(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private async Task SaveTextAsync(string key, string text)
    {
        using var content = new MemoryStream(Encoding.UTF8.GetBytes(text));
        await _storage.SaveAsync(key, content, "text/plain");
    }

    private async Task<string?> ReadTextAsync(string key)
    {
        await using var stream = await _storage.OpenReadAsync(key);
        if (stream == null) return null;
        using var reader = new StreamReader(stream);
        return await reader.ReadToEndAsync();
    }

    [Fact]
    public async Task SaveOpenDelete_RoundTrip()
    {
        await _storage.InitializeAsync();
        Assert.True(Directory.Exists(_root));

        await SaveTextAsync("cv/1/a.pdf", "first");
        await SaveTextAsync("cv/1/a.pdf", "second");

        Assert.Equal("second", await ReadTextAsync("cv/1/a.pdf"));
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "cv", "1")));

        await _storage.DeleteAsync("cv/1/a.pdf");

        Assert.Null(await ReadTextAsync("cv/1/a.pdf"));
        // puste katalogi po ostatnim pliku są usuwane
        Assert.False(Directory.Exists(Path.Combine(_root, "cv")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task OpenAndDelete_MissingFile_AreNotErrors()
    {
        Assert.Null(await _storage.OpenReadAsync("cv/9/missing.pdf"));
        await _storage.DeleteAsync("cv/9/missing.pdf");
        await _storage.DeleteByPrefixAsync("cv/");
    }

    [Fact]
    public async Task DeleteByPrefix_RemovesOnlyMatchingFiles()
    {
        await SaveTextAsync("cv/1/a.pdf", "a");
        await SaveTextAsync("cv/2/b.docx", "b");
        await SaveTextAsync("other/c.txt", "c");

        await _storage.DeleteByPrefixAsync("cv/");

        Assert.Null(await ReadTextAsync("cv/1/a.pdf"));
        Assert.Null(await ReadTextAsync("cv/2/b.docx"));
        Assert.Equal("c", await ReadTextAsync("other/c.txt"));
    }

    [Fact]
    public async Task Save_WhenContentFails_LeavesNoFile()
    {
        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync("cv/1/a.pdf", new FailingStream(), "application/pdf"));

        Assert.Null(await _storage.OpenReadAsync("cv/1/a.pdf"));
        Assert.Empty(Directory.GetFiles(_root, "*", SearchOption.AllDirectories));
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("cv/../../outside.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("cv\\1\\a.pdf")]
    [InlineData("cv/../cv/a.pdf")]
    [InlineData("cv/./a.pdf")]
    [InlineData("cv//a.pdf")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("cv/a.pdf:stream")]
    [InlineData("cv/a\r\n.pdf")]
    [InlineData("..")]
    [InlineData("")]
    public async Task InvalidKeys_AreRejected(string key)
    {
        using var content = new MemoryStream([1, 2, 3]);

        await Assert.ThrowsAsync<ArgumentException>(() => _storage.SaveAsync(key, content, "application/pdf"));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.OpenReadAsync(key));
        await Assert.ThrowsAsync<ArgumentException>(() => _storage.DeleteAsync(key));
        Assert.False(Directory.Exists(_root) && Directory.EnumerateFileSystemEntries(_root).Any());
    }

    private sealed class FailingStream : MemoryStream
    {
        private int _reads;

        public FailingStream() : base(new byte[1024])
        {
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            _reads++ == 0 ? base.ReadAsync(buffer[..10], cancellationToken) : throw new IOException("Client disconnected");
    }
}
