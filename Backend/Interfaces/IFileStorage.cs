namespace Backend.Interfaces;

// Magazyn plików (CV kandydatów). Klucze generuje serwer (np. "cv/{applicationId}/{guid}.pdf") - nigdy z nazwy
// pliku użytkownika; segmenty rozdzielone "/".
public interface IFileStorage
{
    // przygotowanie magazynu przy starcie aplikacji (katalog Local, bucket S3 tworzony, jeśli nie istnieje)
    Task InitializeAsync(CancellationToken cancellationToken = default);

    // zapis (nadpisuje plik o tym samym kluczu); przerwany zapis nie zostawia częściowego pliku
    Task SaveAsync(string key, Stream content, string contentType, CancellationToken cancellationToken = default);

    // strumień do odczytu albo null, gdy pliku nie ma; wywołujący zwalnia strumień
    Task<Stream?> OpenReadAsync(string key, CancellationToken cancellationToken = default);

    // usunięcie nieistniejącego pliku nie jest błędem
    Task DeleteAsync(string key, CancellationToken cancellationToken = default);

    // usuwa wszystkie pliki z kluczem zaczynającym się od prefiksu (reset danych demo)
    Task DeleteByPrefixAsync(string prefix, CancellationToken cancellationToken = default);
}
