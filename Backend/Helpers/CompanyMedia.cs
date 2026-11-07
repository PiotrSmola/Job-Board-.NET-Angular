using Microsoft.AspNetCore.Http.Features;
using Microsoft.Net.Http.Headers;

namespace Backend.Helpers;

// Wynik odczytu formularza z obrazami: pliki albo komunikat błędu (400)
public sealed record ImageUploadFormResult(IReadOnlyList<IFormFile> Files, string? Error)
{
    public static ImageUploadFormResult Failed(string error) => new([], error);
}

// Media profilu firmy (galeria zdjęć i tło ofert): limity, komunikaty, klucze w magazynie, adresy plików i odczyt
// formularzy uploadu. Formularz jest czytany w akcji dopiero po ustaleniu firmy pracodawcy - jak przy załącznikach
// (endpoint z [RequestSizeLimit(AttachmentFiles.MaxRequestSize)] i [DisableFormValueModelBinding]).
public static class CompanyMedia
{
    public const int MaxPhotos = 20;
    public const int MaxCaptionLength = 150;

    public const int MinBackgroundDim = 55;
    public const int MaxBackgroundDim = 95;
    public const int DefaultBackgroundDim = 75;

    // pola formularzy: zdjęcia - jedno lub więcej pól "files", tło - jedno pole "file"
    public const string PhotosField = "files";
    public const string BackgroundField = "file";

    // wspólny prefiks kluczy plików firm w magazynie (reset danych demo usuwa wszystko pod nim)
    public const string StorageKeyPrefix = "companies/";

    public const string TooManyPhotosMessage = "A company can have at most 20 photos";
    public const string PhotoNotFoundMessage = "Photo not found";
    public const string InvalidOrderMessage = "Invalid photo order";
    public const string BackgroundNotFoundMessage = "Offer background not found";
    public const string RequestTooLargeMessage = "Request is too large - upload at most 25 MB at once";
    public const string NoPhotosMessage = "No file uploaded - send images in the 'files' form field";
    public const string NoBackgroundMessage = "No file uploaded - send the image in the 'file' form field";

    // zdjęcia z formularza: pliki i nic więcej; ValueCountLimit o jeden większy niż limit zdjęć - 21. plik dostaje
    // komunikat o limicie, a nie błąd formularza
    private static readonly FormOptions PhotoFormOptions = FormOptions(MaxPhotos + 1);

    private static readonly FormOptions BackgroundFormOptions = FormOptions(2);

    public static string NewPhotoStorageKey(int companyId, AttachmentFileType type) =>
        $"{StorageKeyPrefix}{companyId}/photos/{Guid.NewGuid():N}{type.Extension}";

    public static string NewBackgroundStorageKey(int companyId, AttachmentFileType type) =>
        $"{StorageKeyPrefix}{companyId}/offer-background/{Guid.NewGuid():N}{type.Extension}";

    public static string PhotoUrl(int companyId, int photoId) => $"/api/companies/{companyId}/photos/{photoId}/file";

    // Adres tła z wersją (GUID z klucza pliku): nowe tło ma nowy adres, więc obraz z publicznego cache (1 dzień) nie
    // zostaje po zmianie; endpoint ignoruje parametr v
    public static string BackgroundUrl(int companyId, string backgroundKey) =>
        $"/api/companies/{companyId}/offer-background/file?v={Path.GetFileNameWithoutExtension(backgroundKey)}";

    // Cache-Control pliku: obrazy firmy zatwierdzonej są publiczne (zdjęcie ma stały adres, tło - adres z wersją),
    // pozostałe widzą tylko admin i członkowie firmy - bez zapisu w żadnym cache
    public static string CacheControl(bool isPublic) => isPublic ? "public, max-age=86400" : "private, no-store";

    // Content-Disposition: wyświetlenie w przeglądarce (nazwa tylko z identyfikatorów i rozszerzenia typu)
    public static string InlineDisposition(string fileName) =>
        new ContentDispositionHeaderValue("inline") { FileName = fileName }.ToString();

    public static string? NormalizeCaption(string? caption)
    {
        if (caption == null)
        {
            return null;
        }

        var normalized = TextNormalizer.Normalize(caption);
        return normalized.Length == 0 ? null : normalized;
    }

    public static async Task<ImageUploadFormResult> ReadPhotosAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var (form, error) = await AttachmentUploadForm.ReadFormAsync(context, PhotoFormOptions, RequestTooLargeMessage,
            NoPhotosMessage, cancellationToken);
        if (form == null)
        {
            return ImageUploadFormResult.Failed(error!);
        }

        if (form.Files.Count == 0 || form.Files.Any(f => f.Name != PhotosField))
        {
            return ImageUploadFormResult.Failed(NoPhotosMessage);
        }

        if (form.Files.Count > MaxPhotos)
        {
            return ImageUploadFormResult.Failed(TooManyPhotosMessage);
        }

        return new ImageUploadFormResult(form.Files.ToList(), null);
    }

    public static async Task<ImageUploadFormResult> ReadBackgroundAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var (form, error) = await AttachmentUploadForm.ReadFormAsync(context, BackgroundFormOptions, RequestTooLargeMessage,
            NoBackgroundMessage, cancellationToken);
        if (form == null)
        {
            return ImageUploadFormResult.Failed(error!);
        }

        if (form.Files.Count != 1 || form.Files[0].Name != BackgroundField)
        {
            return ImageUploadFormResult.Failed(NoBackgroundMessage);
        }

        return new ImageUploadFormResult(form.Files.ToList(), null);
    }

    private static FormOptions FormOptions(int valueCountLimit) => new()
    {
        // pojedyncza część - plik większy niż 5 MB ma dostać czytelny komunikat, nie błąd formularza
        MultipartBodyLengthLimit = AttachmentFiles.MaxRequestSize,
        MultipartBoundaryLengthLimit = 100,
        MultipartHeadersCountLimit = 8,
        MultipartHeadersLengthLimit = 4 * 1024,
        ValueCountLimit = valueCountLimit,
        // formularz to same pliki (ewentualne pola tekstowe są ignorowane, ale liczą się do ValueCountLimit)
        ValueLengthLimit = 64,
        KeyLengthLimit = 64
    };
}
