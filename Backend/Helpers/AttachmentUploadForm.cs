using System.Globalization;
using Backend.Models;
using Microsoft.AspNetCore.Http.Features;

namespace Backend.Helpers;

// Plik z formularza i jego rodzaj (pole "kinds" o tym samym indeksie)
public sealed record AttachmentUploadItem(IFormFile File, AttachmentKind Kind);

// Komunikaty limitów (liczba plików, łączny rozmiar) - załączniki aplikacji albo dokumenty profilu
public sealed record AttachmentLimitMessages(string TooMany, string TotalTooLarge)
{
    public static readonly AttachmentLimitMessages Application = new(AttachmentFiles.TooManyMessage, AttachmentFiles.TotalTooLargeMessage);

    public static readonly AttachmentLimitMessages Profile =
        new(AttachmentFiles.ProfileTooManyMessage, AttachmentFiles.ProfileTotalTooLargeMessage);
}

// Wynik odczytu formularza: pliki albo komunikat błędu (400)
public sealed record AttachmentUploadFormResult(IReadOnlyList<AttachmentUploadItem> Items, string? Error)
{
    public static AttachmentUploadFormResult Failed(string error) => new([], error);
}

// Odczyt formularza uploadu załączników i dokumentów profilu (multipart/form-data: pola "files" i równoległa lista
// "kinds"). Wywoływany w akcji dopiero po sprawdzeniu właściciela i statusu aplikacji - obcy nie wysyłają 26 MB na cudze id.
// Endpoint musi mieć [RequestSizeLimit(AttachmentFiles.MaxRequestSize)] (RequestBodyLimitMiddleware). Limity formularza
// tylko dla tego endpointu; każdy plik z formularza jest buforowany przez ASP.NET Core (powyżej 64 KB na dysku,
// w pliku tymczasowym usuwanym po żądaniu), dzięki czemu strumień pliku da się przewijać przy wykrywaniu typu.
public static class AttachmentUploadForm
{
    public const string FilesField = "files";
    public const string KindsField = "kinds";

    private static readonly FormOptions Options = new()
    {
        // pojedyncza część - plik większy niż 5 MB ma dostać czytelny komunikat, nie błąd formularza
        MultipartBodyLengthLimit = AttachmentFiles.MaxRequestSize,
        MultipartBoundaryLengthLimit = 100,
        MultipartHeadersCountLimit = 8,
        MultipartHeadersLengthLimit = 4 * 1024,
        // limit części formularza obejmuje pliki i pola tekstowe razem - frontend wysyła dla każdego pliku pole
        // "files" i pole "kinds", więc mieści się 11 par (o jedną więcej niż limit załączników, żeby 11. plik dostał
        // komunikat o limicie, a nie błąd formularza)
        ValueCountLimit = 2 * (AttachmentFiles.MaxAttachments + 1),
        // pola tekstowe (kinds) to krótkie liczby; treść plików ogranicza MultipartBodyLengthLimit
        ValueLengthLimit = 64,
        KeyLengthLimit = 64
    };

    public static Task<AttachmentUploadFormResult> ReadAsync(HttpContext context, CancellationToken cancellationToken) =>
        ReadAsync(context, AttachmentLimitMessages.Application, cancellationToken);

    public static async Task<AttachmentUploadFormResult> ReadAsync(HttpContext context, AttachmentLimitMessages messages,
        CancellationToken cancellationToken)
    {
        var request = context.Request;

        // limit całego żądania (26 MB) ustawia RequestBodyLimitMiddleware z atrybutu [RequestSizeLimit] akcji - także
        // niezależnie od serwera (strumień z limitem); przekroczenie w trakcie odczytu to 413 zamieniane niżej na
        // komunikat o łącznym rozmiarze
        if (request.ContentLength > AttachmentFiles.MaxRequestSize)
        {
            return AttachmentUploadFormResult.Failed(messages.TotalTooLarge);
        }

        if (!request.HasFormContentType)
        {
            return AttachmentUploadFormResult.Failed(AttachmentFiles.NoFileMessage);
        }

        IFormCollection form;
        try
        {
            context.Features.Set<IFormFeature>(new FormFeature(request, Options));
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return AttachmentUploadFormResult.Failed(messages.TotalTooLarge);
        }
        catch (Exception ex) when (ex is InvalidDataException or IOException)
        {
            // uszkodzony albo ucięty multipart, przekroczony limit części lub bufora (BadHttpRequestException
            // jest IOException)
            return AttachmentUploadFormResult.Failed(AttachmentFiles.InvalidFormMessage);
        }

        var files = form.Files;
        if (files.Count == 0 || files.Any(f => f.Name != FilesField))
        {
            return AttachmentUploadFormResult.Failed(AttachmentFiles.NoFileMessage);
        }

        if (files.Count > AttachmentFiles.MaxAttachments)
        {
            return AttachmentUploadFormResult.Failed(messages.TooMany);
        }

        // rodzaj i-tego pliku z i-tego pola "kinds"; brak (albo za mało pól) - Other, więcej pól niż plików
        // albo wartość spoza enuma - błąd
        var kinds = form[KindsField];
        if (kinds.Count > files.Count)
        {
            return AttachmentUploadFormResult.Failed(AttachmentFiles.InvalidKindMessage);
        }

        var items = new List<AttachmentUploadItem>(files.Count);
        for (var i = 0; i < files.Count; i++)
        {
            var kind = AttachmentKind.Other;
            if (i < kinds.Count && !TryParseKind(kinds[i], out kind))
            {
                return AttachmentUploadFormResult.Failed(AttachmentFiles.InvalidKindMessage);
            }

            items.Add(new AttachmentUploadItem(files[i], kind));
        }

        return new AttachmentUploadFormResult(items, null);
    }

    // Rozmiar i typ pliku (po zawartości): wykryty typ albo komunikat błędu
    public static async Task<(string? Error, AttachmentFileType? Type)> ValidateFileAsync(IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return (AttachmentFiles.EmptyFileMessage, null);
        }

        if (file.Length > AttachmentFiles.MaxFileSize)
        {
            return (AttachmentFiles.TooLargeMessage, null);
        }

        // strumień pliku z formularza jest przewijalny (bufor ASP.NET Core)
        await using var content = file.OpenReadStream();
        var type = await AttachmentFiles.DetectTypeAsync(content, cancellationToken);
        return type == null ? (AttachmentFiles.UnsupportedTypeMessage, null) : (null, type);
    }

    private static bool TryParseKind(string? value, out AttachmentKind kind)
    {
        kind = AttachmentKind.Other;
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var number)
            || !Enum.IsDefined(typeof(AttachmentKind), number))
        {
            return false;
        }

        kind = (AttachmentKind)number;
        return true;
    }
}
