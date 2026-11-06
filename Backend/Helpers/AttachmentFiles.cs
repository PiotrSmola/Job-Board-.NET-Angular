using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace Backend.Helpers;

// Typ załącznika wykryty po zawartości pliku (rozszerzenie i content type ustala serwer)
public sealed record AttachmentFileType(string Extension, string ContentType)
{
    public static readonly AttachmentFileType Pdf = new(".pdf", "application/pdf");

    public static readonly AttachmentFileType Docx = new(".docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document");

    public static readonly AttachmentFileType Png = new(".png", "image/png");

    public static readonly AttachmentFileType Jpeg = new(".jpg", "image/jpeg");
}

// Walidacja, limity i nazewnictwo plików załączników aplikacji
public static class AttachmentFiles
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    // suma rozmiarów załączników jednej aplikacji (istniejące + nowe)
    public const long MaxTotalSize = 25 * 1024 * 1024;

    public const int MaxAttachments = 10;

    // limit całego żądania multipart (pliki + nagłówki części) - większe żądanie jest odrzucane bez czytania treści
    public const long MaxRequestSize = 26 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    // nazwa użyta, gdy po sanityzacji z nazwy pliku nic nie zostaje
    public const string FallbackFileName = "attachment";

    // wspólny prefiks kluczy plików załączników w magazynie
    public const string StorageKeyPrefix = "attachments/";

    // DOCX: limity z katalogu centralnego ZIP (treść nigdy nie jest rozpakowywana) - ochrona przed zip bombą,
    // plikami z ogromną liczbą wpisów i katalogiem centralnym wczytywanym w całości do pamięci
    public const int MaxDocxEntries = 1000;
    public const long MaxDocxUncompressedSize = 50 * 1024 * 1024;
    public const long MaxDocxCentralDirectorySize = 256 * 1024;

    public const string UnsupportedTypeMessage = "Unsupported file type - upload a PDF, DOCX, PNG or JPEG file";
    public const string TooLargeMessage = "File is too large - maximum size is 5 MB";
    public const string EmptyFileMessage = "File is empty";
    public const string TotalTooLargeMessage = "Total size of attachments cannot exceed 25 MB";
    public const string TooManyMessage = "An application can have at most 10 attachments";
    public const string NoFileMessage = "No file uploaded - send files in the 'files' form field";
    public const string InvalidFormMessage = "Invalid multipart form data";
    public const string InvalidKindMessage = "Invalid attachment kind";
    public const string StatusMessage = "Attachments cannot be added in the current application status";
    public const string NotFoundMessage = "Attachment not found";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
    private static readonly byte[] JpegSignature = [0xFF, 0xD8, 0xFF];
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    // Nazwy urządzeń zarezerwowane w Windows - pobrany plik "CON.pdf" nie dałby się zapisać
    private static readonly HashSet<string> ReservedWindowsNames = new(
        new[] { "CON", "PRN", "AUX", "NUL" }
            .Concat(Enumerable.Range(1, 9).Select(i => $"COM{i}"))
            .Concat(Enumerable.Range(1, 9).Select(i => $"LPT{i}")),
        StringComparer.OrdinalIgnoreCase);

    // rozszerzenia zastępowane rozszerzeniem wykrytego typu (także niezgodne z treścią, np. .pdf dla DOCX)
    private static readonly HashSet<string> KnownExtensions = new(
        [".pdf", ".docx", ".doc", ".png", ".jpg", ".jpeg"], StringComparer.OrdinalIgnoreCase);

    // Rozpoznaje PDF (%PDF-), PNG i JPEG (sygnatury) oraz DOCX (ZIP z [Content_Types].xml i katalogiem word/).
    // Strumień musi być przewijalny (ZIP jest czytany od katalogu centralnego na końcu pliku, bez rozpakowywania);
    // po wywołaniu pozycja wraca na początek.
    public static async Task<AttachmentFileType?> DetectTypeAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek)
        {
            throw new ArgumentException("Attachment content stream must be seekable", nameof(content));
        }

        try
        {
            content.Position = 0;
            var header = new byte[PngSignature.Length];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

            if (StartsWith(header, read, PdfSignature))
            {
                return AttachmentFileType.Pdf;
            }

            if (StartsWith(header, read, PngSignature))
            {
                return AttachmentFileType.Png;
            }

            if (StartsWith(header, read, JpegSignature))
            {
                return AttachmentFileType.Jpeg;
            }

            if (StartsWith(header, read, ZipSignature) && IsWordDocument(content))
            {
                return AttachmentFileType.Docx;
            }

            return null;
        }
        finally
        {
            content.Position = 0;
        }
    }

    // Nazwa do wyświetlenia i w Content-Disposition: bez ścieżki, znaków sterujących i formatujących (np. odwrócenie
    // kierunku tekstu), znaków niedozwolonych w nazwach plików i nazw zarezerwowanych Windows; rozszerzenie zgodne
    // z wykrytym typem; max 255 znaków
    public static string SanitizeFileName(string? fileName, AttachmentFileType type)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Normalize(NormalizationForm.FormC);

        // 1. znaki sterujące i formatujące są usuwane, niedozwolone zastępowane "_"
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            var category = char.GetUnicodeCategory(c);
            if (category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator)
            {
                continue;
            }

            builder.Append(c is '"' or '<' or '>' or ':' or '*' or '?' or '|' ? '_' : c);
        }

        // 2. dopiero potem spacje i kropki na końcu (razem - ". . ." nie może zostawić ". . ")
        var baseName = TrimEndDotsAndSpaces(builder.ToString().TrimStart());
        var extension = Path.GetExtension(baseName);

        // inne kropki w nazwie zostają ("Jan.Kowalski CV" -> "Jan.Kowalski CV.pdf")
        if (KnownExtensions.Contains(extension))
        {
            baseName = TrimEndDotsAndSpaces(baseName[..^extension.Length]);
        }

        if (baseName.Length == 0)
        {
            baseName = FallbackFileName;
        }

        // Windows traktuje "CON.pdf" i "con.tar" jak urządzenie - liczy się część przed pierwszą kropką
        var stem = baseName.Split('.')[0].TrimEnd();
        if (ReservedWindowsNames.Contains(stem))
        {
            baseName = "_" + baseName;
        }

        var maxBaseLength = MaxFileNameLength - type.Extension.Length;
        if (baseName.Length > maxBaseLength)
        {
            // bez rozcinania pary surogatów (znaki spoza BMP, np. emoji)
            var cut = char.IsLowSurrogate(baseName[maxBaseLength]) ? maxBaseLength - 1 : maxBaseLength;
            baseName = TrimEndDotsAndSpaces(baseName[..cut]);
        }

        return baseName + type.Extension;
    }

    // Klucz w magazynie generowany przez serwer - nigdy z nazwy pliku użytkownika
    public static string NewStorageKey(int applicationId, AttachmentFileType type) =>
        $"{StorageKeyPrefix}{applicationId}/{Guid.NewGuid():N}{type.Extension}";

    // Content-Disposition: attachment z nazwą w UTF-8 (filename*, RFC 5987/6266) - budowany przez
    // ContentDispositionHeaderValue, który koduje każdy znak spoza dozwolonych (także CR/LF i cudzysłowy)
    public static string ContentDisposition(string fileName) =>
        new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName }.ToString();

    private static string TrimEndDotsAndSpaces(string value)
    {
        var end = value.Length;
        while (end > 0 && (value[end - 1] == '.' || char.IsWhiteSpace(value[end - 1])))
        {
            end--;
        }

        return value[..end];
    }

    private static bool StartsWith(byte[] buffer, int length, byte[] signature) =>
        length >= signature.Length && buffer.AsSpan(0, signature.Length).SequenceEqual(signature);

    // Tylko katalog centralny ZIP (nazwy i zadeklarowane rozmiary wpisów) - bez rozpakowywania treści
    private static bool IsWordDocument(Stream content)
    {
        try
        {
            // rekord końca katalogu centralnego sprawdzany, zanim ZipArchive wczyta katalog do pamięci
            if (!HasAcceptableCentralDirectory(content))
            {
                return false;
            }

            content.Position = 0;
            using var archive = new ZipArchive(content, ZipArchiveMode.Read, leaveOpen: true);

            var hasContentTypes = false;
            var hasWordPart = false;
            var entries = 0;
            long uncompressedSize = 0;
            foreach (var entry in archive.Entries)
            {
                entries++;
                uncompressedSize += entry.Length;
                if (entries > MaxDocxEntries || entry.Length < 0 || uncompressedSize > MaxDocxUncompressedSize)
                {
                    return false;
                }

                hasContentTypes |= entry.FullName == "[Content_Types].xml";
                hasWordPart |= entry.FullName.StartsWith("word/", StringComparison.Ordinal);
            }

            return hasContentTypes && hasWordPart;
        }
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException or NotSupportedException)
        {
            // uszkodzony albo niepełny ZIP
            return false;
        }
    }

    // Rekord EOCD (sygnatura PK 05 06, ostatnie 22 B + komentarz do 64 KB): jeden dysk, bez ZIP64 (0xFFFF /
    // 0xFFFFFFFF), 1-1000 wpisów, katalog centralny do 256 KB, w całości przed rekordem EOCD i nie mniejszy niż
    // minimalny rozmiar zadeklarowanej liczby wpisów (46 B na wpis)
    private static bool HasAcceptableCentralDirectory(Stream content)
    {
        const int eocdSize = 22;
        const int minCentralDirectoryEntrySize = 46;

        var tailLength = (int)Math.Min(content.Length, eocdSize + ushort.MaxValue);
        if (tailLength < eocdSize)
        {
            return false;
        }

        var tail = new byte[tailLength];
        content.Position = content.Length - tailLength;
        content.ReadExactly(tail);

        for (var i = tailLength - eocdSize; i >= 0; i--)
        {
            if (tail[i] != 0x50 || tail[i + 1] != 0x4B || tail[i + 2] != 0x05 || tail[i + 3] != 0x06)
            {
                continue;
            }

            var record = tail.AsSpan(i, eocdSize);
            var diskNumber = BinaryPrimitives.ReadUInt16LittleEndian(record[4..]);
            var centralDirectoryDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[6..]);
            var entriesOnDisk = BinaryPrimitives.ReadUInt16LittleEndian(record[8..]);
            var totalEntries = BinaryPrimitives.ReadUInt16LittleEndian(record[10..]);
            var centralDirectorySize = BinaryPrimitives.ReadUInt32LittleEndian(record[12..]);
            var centralDirectoryOffset = BinaryPrimitives.ReadUInt32LittleEndian(record[16..]);
            long eocdPosition = content.Length - tailLength + i;

            return diskNumber == 0
                   && centralDirectoryDisk == 0
                   && entriesOnDisk == totalEntries
                   && totalEntries is > 0 and <= MaxDocxEntries
                   && centralDirectorySize != uint.MaxValue
                   && centralDirectoryOffset != uint.MaxValue
                   && centralDirectorySize <= MaxDocxCentralDirectorySize
                   && centralDirectorySize >= (long)totalEntries * minCentralDirectoryEntrySize
                   && (long)centralDirectoryOffset + centralDirectorySize <= eocdPosition;
        }

        return false;
    }
}
