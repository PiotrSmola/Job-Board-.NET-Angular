using System.Buffers.Binary;
using System.Globalization;
using System.IO.Compression;
using System.Text;
using Microsoft.Net.Http.Headers;

namespace Backend.Helpers;

// Typ CV wykryty po zawartości pliku (rozszerzenie i content type ustala serwer)
public sealed record CvFileType(string Extension, string ContentType)
{
    public static readonly CvFileType Pdf = new(".pdf", "application/pdf");

    public static readonly CvFileType Docx = new(".docx",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document");
}

// Walidacja i nazewnictwo plików CV
public static class CvFiles
{
    public const long MaxFileSize = 5 * 1024 * 1024;

    // limit całego żądania multipart (plik + nagłówki części) - większe żądanie jest odrzucane bez czytania treści
    public const long MaxRequestSize = 6 * 1024 * 1024;

    public const int MaxFileNameLength = 255;

    // wspólny prefiks kluczy plików CV w magazynie
    public const string StorageKeyPrefix = "cv/";

    // DOCX: limity z katalogu centralnego ZIP (treść nigdy nie jest rozpakowywana) - ochrona przed zip bombą
    // i plikami z ogromną liczbą wpisów
    public const int MaxDocxEntries = 1000;
    public const long MaxDocxUncompressedSize = 50 * 1024 * 1024;

    public const string UnsupportedTypeMessage = "Unsupported file type - upload a PDF or DOCX file";
    public const string TooLargeMessage = "File is too large - maximum size is 5 MB";
    public const string EmptyFileMessage = "File is empty";

    private static readonly byte[] PdfSignature = "%PDF-"u8.ToArray();
    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];

    // Rozpoznaje PDF (%PDF- na początku) i DOCX (ZIP z [Content_Types].xml i katalogiem word/).
    // Strumień musi być przewijalny (ZIP jest czytany od katalogu centralnego na końcu pliku, bez rozpakowywania);
    // po wywołaniu pozycja wraca na początek.
    public static async Task<CvFileType?> DetectTypeAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek)
        {
            throw new ArgumentException("CV content stream must be seekable", nameof(content));
        }

        try
        {
            content.Position = 0;
            var header = new byte[Math.Max(PdfSignature.Length, ZipSignature.Length)];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

            if (StartsWith(header, read, PdfSignature))
            {
                return CvFileType.Pdf;
            }

            if (StartsWith(header, read, ZipSignature) && IsWordDocument(content))
            {
                return CvFileType.Docx;
            }

            return null;
        }
        finally
        {
            content.Position = 0;
        }
    }

    // Nazwa do wyświetlenia i w Content-Disposition: bez ścieżki, znaków sterujących i formatujących (np. odwrócenie
    // kierunku tekstu), znaków niedozwolonych w nazwach plików; rozszerzenie zgodne z wykrytym typem; max 255 znaków
    public static string SanitizeFileName(string? fileName, CvFileType type)
    {
        var name = (fileName ?? string.Empty).Replace('\\', '/');
        name = name[(name.LastIndexOf('/') + 1)..].Normalize(NormalizationForm.FormC);

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

        var baseName = builder.ToString().Trim().TrimEnd('.');
        var extension = Path.GetExtension(baseName);

        // rozszerzenie dokumentu (także niezgodne z treścią, np. .pdf dla DOCX) jest zastępowane wykrytym;
        // inne kropki w nazwie zostają ("Jan.Kowalski CV" -> "Jan.Kowalski CV.pdf")
        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".docx", StringComparison.OrdinalIgnoreCase)
            || extension.Equals(".doc", StringComparison.OrdinalIgnoreCase))
        {
            baseName = baseName[..^extension.Length].TrimEnd().TrimEnd('.');
        }

        if (baseName.Length == 0)
        {
            baseName = "cv";
        }

        var maxBaseLength = MaxFileNameLength - type.Extension.Length;
        if (baseName.Length > maxBaseLength)
        {
            // bez rozcinania pary surogatów (znaki spoza BMP, np. emoji)
            var cut = char.IsLowSurrogate(baseName[maxBaseLength]) ? maxBaseLength - 1 : maxBaseLength;
            baseName = baseName[..cut].TrimEnd();
        }

        return baseName + type.Extension;
    }

    // Klucz w magazynie generowany przez serwer - nigdy z nazwy pliku użytkownika
    public static string NewStorageKey(int applicationId, CvFileType type) =>
        $"{StorageKeyPrefix}{applicationId}/{Guid.NewGuid():N}{type.Extension}";

    // Content-Disposition: attachment z nazwą w UTF-8 (filename*, RFC 5987/6266) - budowany przez
    // ContentDispositionHeaderValue, który koduje każdy znak spoza dozwolonych (także CR/LF i cudzysłowy)
    public static string ContentDisposition(string fileName) =>
        new ContentDispositionHeaderValue("attachment") { FileNameStar = fileName }.ToString();

    private static bool StartsWith(byte[] buffer, int length, byte[] signature) =>
        length >= signature.Length && buffer.AsSpan(0, signature.Length).SequenceEqual(signature);

    // Tylko katalog centralny ZIP (nazwy i zadeklarowane rozmiary wpisów) - bez rozpakowywania treści
    private static bool IsWordDocument(Stream content)
    {
        try
        {
            // liczba wpisów z rekordu końca katalogu centralnego, zanim ZipArchive wczyta wszystkie wpisy do pamięci
            var declaredEntries = ReadDeclaredEntryCount(content);
            if (declaredEntries is null or 0 or > MaxDocxEntries)
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
        catch (Exception ex) when (ex is InvalidDataException or EndOfStreamException or ArgumentException)
        {
            // uszkodzony albo niepełny ZIP
            return false;
        }
    }

    // Liczba wpisów z rekordu EOCD (sygnatura PK 05 06, ostatnie 22 B + komentarz do 64 KB); null - brak rekordu.
    // ZIP64 (0xFFFF) zwraca wartość większą od limitu - dokument CV nie potrzebuje ZIP64.
    private static int? ReadDeclaredEntryCount(Stream content)
    {
        const int eocdSize = 22;
        var tailLength = (int)Math.Min(content.Length, eocdSize + ushort.MaxValue);
        if (tailLength < eocdSize)
        {
            return null;
        }

        var tail = new byte[tailLength];
        content.Position = content.Length - tailLength;
        content.ReadExactly(tail);

        for (var i = tailLength - eocdSize; i >= 0; i--)
        {
            if (tail[i] == 0x50 && tail[i + 1] == 0x4B && tail[i + 2] == 0x05 && tail[i + 3] == 0x06)
            {
                // łączna liczba wpisów w katalogu centralnym (offset 10, little-endian)
                return BinaryPrimitives.ReadUInt16LittleEndian(tail.AsSpan(i + 10, 2));
            }
        }

        return null;
    }
}
