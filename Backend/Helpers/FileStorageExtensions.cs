using Backend.Interfaces;

namespace Backend.Helpers;

public static class FileStorageExtensions
{
    // Sprzątanie po zatwierdzonej zmianie w bazie: błąd usuwania pliku jest tylko logowany (operacja się udała,
    // co najwyżej zostaje osierocony plik). Bez tokenu anulowania - przerwane żądanie nie przerywa sprzątania.
    public static async Task DeleteQuietlyAsync(this IFileStorage storage, IEnumerable<string?> keys, ILogger logger)
    {
        foreach (var key in keys)
        {
            if (string.IsNullOrEmpty(key))
            {
                continue;
            }

            try
            {
                await storage.DeleteAsync(key, CancellationToken.None);
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
