using Backend.Interfaces;

namespace Backend.Helpers;

public static class FileStorageExtensions
{
    // czas na usunięcie jednego pliku przy sprzątaniu - niezależny od żądania, ale ograniczony
    public static readonly TimeSpan CleanupTimeout = TimeSpan.FromSeconds(10);

    // Sprzątanie po zatwierdzonej zmianie w bazie (albo po odrzuconym zapisie): błąd usuwania pliku jest tylko
    // logowany (operacja się udała, co najwyżej zostaje osierocony plik). Bez tokenu żądania - przerwane żądanie nie
    // przerywa sprzątania - ale z krótkim limitem czasu na plik, żeby zawieszony magazyn nie blokował odpowiedzi.
    public static async Task DeleteQuietlyAsync(this IFileStorage storage, IEnumerable<string?> keys, ILogger logger)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            using var timeout = new CancellationTokenSource(CleanupTimeout);
            try
            {
                await storage.DeleteAsync(key, timeout.Token);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Failed to delete file {FileKey} from storage", key);
            }
        }
    }

    public static Task DeleteQuietlyAsync(this IFileStorage storage, string? key, ILogger logger) =>
        storage.DeleteQuietlyAsync([key], logger);

    // Kopia pliku pod nowym kluczem (np. dokument profilu dołączany do aplikacji); false - brak pliku źródłowego.
    // Treść jest buforowana w pamięci (najwyżej maxSize bajtów - większy plik to błąd), bo zapis do S3 wymaga
    // przewijalnego strumienia z sumą kontrolną, a strumień odczytu S3 jest jednokierunkowy.
    public static async Task<bool> CopyAsync(this IFileStorage storage, string sourceKey, string destinationKey, string contentType,
        long maxSize, CancellationToken cancellationToken = default)
    {
        using var buffer = new MemoryStream();
        await using (var source = await storage.OpenReadAsync(sourceKey, cancellationToken))
        {
            if (source == null)
            {
                return false;
            }

            var chunk = new byte[81920];
            int read;
            while ((read = await source.Content.ReadAsync(chunk, cancellationToken)) > 0)
            {
                if (buffer.Length + read > maxSize)
                {
                    throw new InvalidDataException($"Stored file {sourceKey} is larger than {maxSize} bytes");
                }

                buffer.Write(chunk, 0, read);
            }
        }

        buffer.Position = 0;
        await storage.SaveAsync(destinationKey, buffer, contentType, cancellationToken);
        return true;
    }
}
