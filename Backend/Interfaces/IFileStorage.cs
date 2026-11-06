namespace Backend.Interfaces;

// Magazyn plików (załączniki aplikacji). Klucze generuje serwer (np. "attachments/{applicationId}/{guid}.pdf") -
// nigdy z nazwy pliku użytkownika; segmenty rozdzielone "/".
public interface IFileStorage
{
    // przygotowanie magazynu przy starcie aplikacji (katalog Local, bucket S3 tworzony, jeśli nie istnieje)
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // zapis (nadpisuje plik o tym samym kluczu); przerwany zapis nie zostawia częściowego pliku
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    // plik do odczytu albo null, gdy go nie ma; wywołujący zwalnia wynik (strumień)
    Task<StoredFile?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    // usunięcie nieistniejącego pliku nie jest błędem
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    // usuwa wszystkie pliki z kluczem zaczynającym się od prefiksu (reset danych demo)
    Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}

// Otwarty plik z magazynu: strumień do odczytu i rozmiar w bajtach, jeśli magazyn go podaje (null - nieznany).
// Zwolnienie zwalnia strumień (i np. odpowiedź HTTP magazynu S3).
public sealed class StoredFile : IDisposable, IAsyncDisposable
{
    public StoredFile(Stream content, long? length)
    {
        Content = content;
        Length = length;
    }

    public Stream Content { get; }

    public long? Length { get; }

    public void Dispose() => Content.Dispose();

    public ValueTask DisposeAsync() => Content.DisposeAsync();
}
