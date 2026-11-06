using Backend.Interfaces;

namespace Backend.Services;

// Pliki na dysku w katalogu z konfiguracji (Storage:LocalPath) - development i testy
public class LocalFileStorage : IFileStorage
{
    // pliki tymczasowe starsze niż ten wiek są przy starcie uznawane za pozostałość po przerwanym zapisie
    public static readonly TimeSpan OrphanedTemporaryFileAge = TimeSpan.FromHours(1);

    private const string TemporaryExtension = ".tmp";

    private readonly string _root;

    // katalogi, do których trwa zapis (licznik zapisów) - usuwanie pustych katalogów je pomija, więc równoległe
    // usunięcie ostatniego pliku aplikacji nie usuwa katalogu, w którym właśnie powstaje nowy plik
    private readonly Dictionary<string, int> _activeWrites = new(StringComparer.Ordinal);
    private readonly object _directoryLock = new();

    public LocalFileStorage(string rootPath)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        DeleteOrphanedTemporaryFiles(DateTime.UtcNow - OrphanedTemporaryFileAge, cancellationToken);
        return Task.CompletedTask;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        var directory = Path.GetDirectoryName(path)!;

        // zapis do pliku tymczasowego w tym samym katalogu (unikalna nazwa) i podmiana - przerwany zapis nie
        // zostawia częściowego pliku pod kluczem
        var temporaryPath = $"{path}.{Guid.NewGuid():N}{TemporaryExtension}";
        BeginWrite(directory);
        try
        {
            await using (var file = CreateFile(temporaryPath))
            {
                await content.CopyToAsync(file, cancellationToken);
                await file.FlushAsync(cancellationToken);
                // dane na dysku przed podmianą - po awarii zasilania pod kluczem nie zostanie pusty plik
                file.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            EndWrite(directory);
            // katalog utworzony tylko dla tego zapisu nie zostaje pusty
            RemoveEmptyDirectories(directory);
            throw;
        }

        EndWrite(directory);
    }

    public Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 81920, useAsync: true);
            return Task.FromResult<StoredFile?>(new StoredFile(stream, stream.Length));
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<StoredFile?>(null);
        }
    }

    public Task DeleteAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        try
        {
            File.Delete(path);
        }
        catch (DirectoryNotFoundException)
        {
            // brak katalogu = brak pliku
            return Task.CompletedTask;
        }

        RemoveEmptyDirectories(Path.GetDirectoryName(path));
        return Task.CompletedTask;
    }

    public Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_root))
        {
            return Task.CompletedTask;
        }

        foreach (var path in Directory.EnumerateFiles(_root, "*", SearchOption.AllDirectories).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var key = Path.GetRelativePath(_root, path).Replace(Path.DirectorySeparatorChar, '/');
            if (key.StartsWith(prefix, StringComparison.Ordinal))
            {
                File.Delete(path);
                RemoveEmptyDirectories(Path.GetDirectoryName(path));
            }
        }

        return Task.CompletedTask;
    }

    // Klucz "a/b/c.pdf" -> ścieżka w katalogu magazynu. Obronnie (klucze generuje serwer) odrzucane są klucze
    // absolutne, z "\", ":" (dysk/strumień NTFS), znakami sterującymi, pustymi segmentami albo "." i "..",
    // a na końcu - każda ścieżka, która po normalizacji nie leży wewnątrz katalogu magazynu
    private string ResolvePath(string key)
    {
        if (string.IsNullOrWhiteSpace(key)
            || key.Contains('\\')
            || key.Contains(':')
            || key.Any(char.IsControl)
            || Path.IsPathRooted(key)
            || key.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new ArgumentException("Invalid storage key", nameof(key));
        }

        var path = Path.GetFullPath(Path.Combine(_root, key));
        if (!path.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            throw new ArgumentException("Invalid storage key", nameof(key));
        }

        return path;
    }

    private void BeginWrite(string directory)
    {
        lock (_directoryLock)
        {
            // najpierw katalog - błąd jego utworzenia nie zostawia zarejestrowanego zapisu
            Directory.CreateDirectory(directory);
            _activeWrites[directory] = _activeWrites.GetValueOrDefault(directory) + 1;
        }
    }

    private void EndWrite(string directory)
    {
        lock (_directoryLock)
        {
            var count = _activeWrites[directory] - 1;
            if (count == 0)
            {
                _activeWrites.Remove(directory);
            }
            else
            {
                _activeWrites[directory] = count;
            }
        }
    }

    // usuwa puste katalogi (np. attachments/{applicationId}) aż do katalogu głównego magazynu, z pominięciem
    // katalogów, w których (lub pod którymi) trwa zapis
    private void RemoveEmptyDirectories(string? directory)
    {
        lock (_directoryLock)
        {
            while (directory != null
                   && directory.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
                   && !HasActiveWrite(directory)
                   && Directory.Exists(directory)
                   && !Directory.EnumerateFileSystemEntries(directory).Any())
            {
                try
                {
                    Directory.Delete(directory);
                }
                catch (IOException)
                {
                    // plik dodał inny proces - katalog zostaje
                    return;
                }

                directory = Path.GetDirectoryName(directory);
            }
        }
    }

    private bool HasActiveWrite(string directory) =>
        _activeWrites.Keys.Any(active =>
            active == directory || active.StartsWith(directory + Path.DirectorySeparatorChar, StringComparison.Ordinal));

    // Pliki *.tmp po zapisach przerwanych awarią procesu (zwykły błąd zapisu usuwa plik tymczasowy od razu)
    private void DeleteOrphanedTemporaryFiles(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        foreach (var path in Directory.EnumerateFiles(_root, "*" + TemporaryExtension, SearchOption.AllDirectories).ToList())
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                if (File.GetLastWriteTimeUtc(path) < olderThanUtc)
                {
                    File.Delete(path);
                    RemoveEmptyDirectories(Path.GetDirectoryName(path));
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // plik w użyciu albo bez uprawnień - spróbujemy przy następnym starcie
            }
        }
    }

    // Katalog może zniknąć między utworzeniem a otwarciem pliku, gdy pusty katalog usuwa inny proces
    // korzystający z tego samego katalogu magazynu - wtedy kolejna próba
    private static FileStream CreateFile(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
            }
            catch (DirectoryNotFoundException) when (attempt < 5)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}
