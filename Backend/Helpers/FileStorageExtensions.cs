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
}
