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
        await using var file = await _storage.OpenReadAsync(key);
        if (file == null) return null;
        using var reader = new StreamReader(file.Content);
        return await reader.ReadToEndAsync();
    }

    private string[] AllFiles() =>
        Directory.Exists(_root) ? Directory.GetFiles(_root, "*", SearchOption.AllDirectories) : [];

    [Fact]
    public async Task SaveOpenDelete_RoundTrip()
    {
        await _storage.InitializeAsync();
        Assert.True(Directory.Exists(_root));

        await SaveTextAsync("attachments/1/a.pdf", "first");
        await SaveTextAsync("attachments/1/a.pdf", "second");

        Assert.Equal("second", await ReadTextAsync("attachments/1/a.pdf"));
        // po zapisie zostaje tylko plik docelowy - bez plików tymczasowych
        Assert.Single(Directory.GetFiles(Path.Combine(_root, "attachments", "1")));

        await _storage.DeleteAsync("attachments/1/a.pdf");

        Assert.Null(await ReadTextAsync("attachments/1/a.pdf"));
        // puste katalogi po ostatnim pliku są usuwane
        Assert.False(Directory.Exists(Path.Combine(_root, "attachments")));
        Assert.True(Directory.Exists(_root));
    }

    [Fact]
    public async Task OpenRead_ReportsTheFileLength()
    {
        await SaveTextAsync("attachments/1/a.pdf", "12345");

        await using var file = await _storage.OpenReadAsync("attachments/1/a.pdf");

        Assert.NotNull(file);
        Assert.Equal(5, file!.Length);
    }

    [Fact]
    public async Task OpenAndDelete_MissingFile_AreNotErrors()
    {
        Assert.Null(await _storage.OpenReadAsync("attachments/9/missing.pdf"));
        await _storage.DeleteAsync("attachments/9/missing.pdf");
        await _storage.DeleteByPrefixAsync("attachments/");
    }

    [Fact]
    public async Task DeleteByPrefix_RemovesOnlyMatchingFiles()
    {
        await SaveTextAsync("attachments/1/a.pdf", "a");
        await SaveTextAsync("attachments/2/b.docx", "b");
        await SaveTextAsync("other/c.txt", "c");

        await _storage.DeleteByPrefixAsync("attachments/");

        Assert.Null(await ReadTextAsync("attachments/1/a.pdf"));
        Assert.Null(await ReadTextAsync("attachments/2/b.docx"));
        Assert.Equal("c", await ReadTextAsync("other/c.txt"));
    }

    [Fact]
    public async Task Save_WhenContentFails_LeavesNoFile()
    {
        await Assert.ThrowsAsync<IOException>(() => _storage.SaveAsync("attachments/1/a.pdf", new FailingStream(), "application/pdf"));

        Assert.Null(await _storage.OpenReadAsync("attachments/1/a.pdf"));
        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Save_WhenCancelled_LeavesNoFile()
    {
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            _storage.SaveAsync("attachments/1/a.pdf", new MemoryStream(new byte[1024]), "application/pdf", cancellation.Token));

        Assert.Empty(AllFiles());
    }

    [Fact]
    public async Task Save_TemporaryFileLivesNextToTheTargetUnderAUniqueName()
    {
        var seen = new List<string>();
        var content = new ObservingStream(() =>
            seen.AddRange(Directory.GetFiles(Path.Combine(_root, "attachments", "1")).Select(Path.GetFileName)!));

        await _storage.SaveAsync("attachments/1/a.pdf", content, "application/pdf");

        // w trakcie zapisu: tylko plik tymczasowy w tym samym katalogu, z unikalną nazwą i rozszerzeniem .tmp
        var temporary = Assert.Single(seen);
        Assert.Matches("^a\\.pdf\\.[0-9a-f]{32}\\.tmp$", temporary);
        Assert.Equal("a.pdf", Path.GetFileName(Assert.Single(AllFiles())));
    }

    [Fact]
    public async Task Initialize_RemovesOrphanedTemporaryFilesOlderThanAnHour()
    {
        await SaveTextAsync("attachments/1/a.pdf", "kept");
        var directory = Path.Combine(_root, "attachments", "1");
        var oldTemporary = Path.Combine(directory, $"b.pdf.{Guid.NewGuid():N}.tmp");
        var freshTemporary = Path.Combine(directory, $"c.pdf.{Guid.NewGuid():N}.tmp");
        var orphanDirectory = Path.Combine(_root, "attachments", "2");
        Directory.CreateDirectory(orphanDirectory);
        var lonelyTemporary = Path.Combine(orphanDirectory, $"d.pdf.{Guid.NewGuid():N}.tmp");
        var oldRegular = Path.Combine(directory, "e.pdf");
        foreach (var path in new[] { oldTemporary, freshTemporary, lonelyTemporary, oldRegular })
        {
            await File.WriteAllTextAsync(path, "x");
        }

        var old = DateTime.UtcNow - LocalFileStorage.OrphanedTemporaryFileAge - TimeSpan.FromMinutes(5);
        File.SetLastWriteTimeUtc(oldTemporary, old);
        File.SetLastWriteTimeUtc(lonelyTemporary, old);
        File.SetLastWriteTimeUtc(oldRegular, old);

        await new LocalFileStorage(_root).InitializeAsync();

        Assert.False(File.Exists(oldTemporary));
        Assert.False(File.Exists(lonelyTemporary));
        // katalog, w którym został tylko osierocony plik tymczasowy, też znika
        Assert.False(Directory.Exists(orphanDirectory));
        // świeży plik tymczasowy może należeć do trwającego zapisu, a zwykłych plików sprzątanie nie dotyka
        Assert.True(File.Exists(freshTemporary));
        Assert.True(File.Exists(oldRegular));
        Assert.Equal("kept", await ReadTextAsync("attachments/1/a.pdf"));
    }

    [Fact]
    public async Task DeletingTheLastFileWhileAnotherIsSavedToTheSameDirectory_NeverBreaksTheSave()
    {
        // wiele plików jednej aplikacji: usunięcie ostatniego pliku katalogu nie może usunąć katalogu, w którym
        // równolegle powstaje nowy plik
        for (var round = 0; round < 50; round++)
        {
            var key = $"attachments/{round}/keep.pdf";
            await SaveTextAsync($"attachments/{round}/old.pdf", "old");

            await Task.WhenAll(
                Task.Run(() => _storage.DeleteAsync($"attachments/{round}/old.pdf")),
                Task.Run(() => SaveTextAsync(key, "new")));

            Assert.Equal("new", await ReadTextAsync(key));
            Assert.Null(await ReadTextAsync($"attachments/{round}/old.pdf"));
        }

        Assert.Equal(50, AllFiles().Length);
    }

    [Fact]
    public async Task ParallelSavesAndDeletesInOneDirectory_LeaveExactlyTheSavedFiles()
    {
        var keys = Enumerable.Range(0, 40).Select(i => $"attachments/7/{i}.pdf").ToArray();

        await Task.WhenAll(keys.Select(key => Task.Run(async () =>
        {
            await SaveTextAsync(key, key);
            if (key.EndsWith("0.pdf") || key.EndsWith("5.pdf"))
            {
                await _storage.DeleteAsync(key);
            }
        })));

        var expected = keys.Where(k => !k.EndsWith("0.pdf") && !k.EndsWith("5.pdf")).ToArray();
        Assert.Equal(expected.Length, AllFiles().Length);
        foreach (var key in expected)
        {
            Assert.Equal(key, await ReadTextAsync(key));
        }
    }

    [Theory]
    [InlineData("../outside.pdf")]
    [InlineData("attachments/../../outside.pdf")]
    [InlineData("/etc/passwd")]
    [InlineData("attachments\\1\\a.pdf")]
    [InlineData("attachments/../attachments/a.pdf")]
    [InlineData("attachments/./a.pdf")]
    [InlineData("attachments//a.pdf")]
    [InlineData("C:/Windows/win.ini")]
    [InlineData("attachments/a.pdf:stream")]
    [InlineData("attachments/a\r\n.pdf")]
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

    // Strumień, który przy pierwszym odczycie (gdy plik tymczasowy już istnieje) wywołuje obserwatora
    private sealed class ObservingStream(Action observe) : MemoryStream(new byte[100])
    {
        private bool _observed;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (!_observed)
            {
                _observed = true;
                observe();
            }

            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
