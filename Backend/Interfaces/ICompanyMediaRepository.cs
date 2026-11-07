using Backend.Models;

namespace Backend.Interfaces;

public enum PhotosAddResult
{
    Added,
    CompanyNotFound,
    TooMany
}

public enum PhotoOrderResult
{
    Reordered,
    CompanyNotFound,
    Invalid
}

// Wynik zmiany obrazu tła: Found = false - firma nie istnieje; PreviousKey - plik poprzedniego tła do usunięcia
// z magazynu po zatwierdzeniu zmiany
public sealed record BackgroundChange(bool Found, string? PreviousKey);

// Galeria zdjęć i tło ofert firmy. Zmiany limitowane (dodanie zdjęć, kolejność, obraz tła) są zapisywane pod blokadą
// wiersza firmy - tę samą blokadę bierze usunięcie firmy, więc odczytane tam klucze plików są kompletne.
public interface ICompanyMediaRepository
{
    // firma bez śledzenia (status, obraz tła i jego ustawienia)
    Task<Company?> FindCompanyAsync(int companyId, CancellationToken cancellationToken = default);

    // zdjęcia w kolejności galerii (SortOrder, Id)
    Task<List<CompanyPhoto>> GetPhotosAsync(int companyId, CancellationToken cancellationToken = default);
    Task<CompanyPhoto?> GetPhotoAsync(int companyId, int photoId, CancellationToken cancellationToken = default);
    Task<int> CountPhotosAsync(int companyId, CancellationToken cancellationToken = default);

    // dodaje zdjęcia na końcu galerii (limit CompanyMedia.MaxPhotos łącznie z istniejącymi)
    Task<PhotosAddResult> AddPhotosAsync(int companyId, IReadOnlyList<CompanyPhoto> photos, CancellationToken cancellationToken = default);

    // klucze, które mają rekordy (sprzątanie po błędzie zapisu)
    Task<IReadOnlyList<string>> GetExistingPhotoFileKeysAsync(IReadOnlyCollection<string> fileKeys, CancellationToken cancellationToken = default);

    // zmieniony podpis albo null, gdy zdjęcia nie ma (w tej firmie)
    Task<CompanyPhoto?> UpdateCaptionAsync(int companyId, int photoId, string? caption, CancellationToken cancellationToken = default);

    // photoIds - wszystkie zdjęcia firmy, każde dokładnie raz
    Task<PhotoOrderResult> ReorderPhotosAsync(int companyId, IReadOnlyList<int> photoIds, CancellationToken cancellationToken = default);

    // klucz pliku usuniętego zdjęcia albo null, gdy zdjęcia nie ma (w tej firmie)
    Task<string?> DeletePhotoAsync(int companyId, int photoId, CancellationToken cancellationToken = default);

    // nowy obraz tła (fileKey = null - usunięcie obrazu; przyciemnienie i punkt zaczepienia zostają)
    Task<BackgroundChange> SetOfferBackgroundAsync(int companyId, string? fileKey, string? contentType,
        CancellationToken cancellationToken = default);

    // czy obraz tła o tym kluczu jest zapisany (sprzątanie po błędzie zapisu)
    Task<bool> IsOfferBackgroundKeyRecordedAsync(int companyId, string fileKey, CancellationToken cancellationToken = default);

    // przyciemnienie i punkt zaczepienia (null - bez zmian); false - firma nie istnieje
    Task<bool> UpdateOfferBackgroundSettingsAsync(int companyId, int? dim, OfferBackgroundFocus? focus,
        CancellationToken cancellationToken = default);
}
