using System.Globalization;
using Backend.Models;
using Microsoft.AspNetCore.Http.Features;

namespace Backend.Helpers;

// Plik z formularza i jego rodzaj (pole "kinds" o tym samym indeksie)
public sealed record AttachmentUploadItem(IFormFile File, AttachmentKind Kind);

// Wynik odczytu formularza: pliki albo komunikat błędu (400)
public sealed record AttachmentUploadFormResult(IReadOnlyList<AttachmentUploadItem> Items, string? Error)
{
    public static AttachmentUploadFormResult Failed(string error) => new([], error);
}

// Odczyt formularza uploadu załączników (multipart/form-data: pola "files" i równoległa lista "kinds").
// Wywoływany w akcji dopiero po sprawdzeniu właściciela i statusu aplikacji - obcy nie wysyłają 26 MB na cudze id.
// Limity tylko dla tego endpointu; każdy plik z formularza jest buforowany przez ASP.NET Core (powyżej 64 KB na dysku,
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

    public static async Task<AttachmentUploadFormResult> ReadAsync(HttpContext context, CancellationToken cancellationToken)
    {
        var request = context.Request;

        // Kestrel: limit całego żądania dla tego endpointu (domyślny ~28,6 MB); niezależnie od serwera (TestServer nie
        // obsługuje tej funkcji, żądanie może nie mieć Content-Length) ciało jest czytane przez strumień z limitem
        var bodySizeFeature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (bodySizeFeature is { IsReadOnly: false })
        {
            bodySizeFeature.MaxRequestBodySize = AttachmentFiles.MaxRequestSize;
        }

        if (request.ContentLength > AttachmentFiles.MaxRequestSize)
        {
            return AttachmentUploadFormResult.Failed(AttachmentFiles.TotalTooLargeMessage);
        }

        if (!request.HasFormContentType)
        {
            return AttachmentUploadFormResult.Failed(AttachmentFiles.NoFileMessage);
        }

        IFormCollection form;
        try
        {
            request.Body = new LengthLimitedStream(request.Body, AttachmentFiles.MaxRequestSize);
            context.Features.Set<IFormFeature>(new FormFeature(request, Options));
            form = await request.ReadFormAsync(cancellationToken);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return AttachmentUploadFormResult.Failed(AttachmentFiles.TotalTooLargeMessage);
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
            return AttachmentUploadFormResult.Failed(AttachmentFiles.TooManyMessage);
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

// Strumień treści żądania tylko do odczytu, który po przekroczeniu limitu bajtów zgłasza 413 (jak Kestrel).
// Nie zwalnia strumienia serwera - jego właścicielem jest serwer.
internal sealed class LengthLimitedStream(Stream inner, long limit) : Stream
{
    private long _read;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => Count(inner.Read(buffer, offset, count));

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
        Count(await inner.ReadAsync(buffer, cancellationToken));

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    private int Count(int read)
    {
        _read += read;
        if (_read > limit)
        {
            throw new BadHttpRequestException("Request body too large", StatusCodes.Status413PayloadTooLarge);
        }

        return read;
    }
}
