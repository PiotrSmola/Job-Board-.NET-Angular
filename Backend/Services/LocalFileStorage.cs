using Backend.Interfaces;

namespace Backend.Services;

// Pliki na dysku w katalogu z konfiguracji (Storage:LocalPath) - development i testy
public class LocalFileStorage : IFileStorage
{
    private readonly string _root;

    public LocalFileStorage(string rootPath)
    {
        _root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(rootPath));
    }

    public Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_root);
        return Task.CompletedTask;
    }

    public async Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);

        // zapis do pliku tymczasowego i podmiana - przerwany zapis nie zostawia częściowego pliku pod kluczem
        var temporaryPath = $"{path}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var file = CreateFile(temporaryPath))
            {
                await content.CopyToAsync(file, cancellationToken);
            }

            File.Move(temporaryPath, path, overwrite: true);
        }
        catch
        {
            TryDeleteFile(temporaryPath);
            throw;
        }
    }

    public Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default)
    {
        var path = ResolvePath(key);
        try
        {
            Stream stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete,
                bufferSize: 81920, useAsync: true);
            return Task.FromResult<Stream?>(stream);
        }
        catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return Task.FromResult<Stream?>(null);
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

    // usuwa puste katalogi (np. cv/{applicationId}) aż do katalogu głównego magazynu
    private void RemoveEmptyDirectories(string? directory)
    {
        while (directory != null
               && directory.StartsWith(_root + Path.DirectorySeparatorChar, StringComparison.Ordinal)
               && Directory.Exists(directory)
               && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            try
            {
                Directory.Delete(directory);
            }
            catch (IOException)
            {
                // równoległy zapis dodał plik - katalog zostaje
                return;
            }

            directory = Path.GetDirectoryName(directory);
        }
    }

    // katalog może zniknąć między utworzeniem a otwarciem pliku (równoległe usunięcie ostatniego pliku
    // z RemoveEmptyDirectories) - wtedy kolejna próba
    private static FileStream CreateFile(string path)
    {
        for (var attempt = 1; ; attempt++)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                    bufferSize: 81920, useAsync: true);
            }
            catch (DirectoryNotFoundException) when (attempt < 3)
            {
            }
        }
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
    }
}
