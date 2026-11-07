using System.Buffers.Binary;

namespace Backend.Helpers;

// Obraz rozpoznany po zawartości: typ (rozszerzenie i content type ustala serwer) i wymiary z nagłówka
public sealed record ImageFileInfo(AttachmentFileType Type, int Width, int Height);

// Wynik walidacji obrazu: informacje o obrazie albo komunikat błędu (400)
public sealed record ImageValidationResult(ImageFileInfo? Image, string? Error);

// Walidacja obrazów (zdjęcia firm, tło ofert): tylko PNG i JPEG rozpoznane po zawartości, szerokość i wysokość
// odczytane z nagłówka (PNG - IHDR, JPEG - znacznik SOFn) bez dekodowania obrazu. Odrzucane są pliki z uszkodzonym
// nagłówkiem i absurdalnymi wymiarami (ochrona przeglądarek przed "bombą dekompresji" - mały plik, ogromny obraz).
public static class ImageFiles
{
    public const long MaxFileSize = AttachmentFiles.MaxFileSize;

    // najdłuższy bok i liczba pikseli (40 MP - np. 8000 x 5000)
    public const int MaxDimension = 12000;
    public const long MaxPixels = 40_000_000;

    public const string UnsupportedTypeMessage = "Unsupported image type - upload a PNG or JPEG file";
    public const string TooLargeMessage = AttachmentFiles.TooLargeMessage;
    public const string EmptyFileMessage = AttachmentFiles.EmptyFileMessage;
    public const string DimensionsMessage = "Image dimensions are too large - maximum is 12000 px per side and 40 megapixels";

    private static readonly byte[] PngSignature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Rozmiar i zawartość pliku z formularza (strumień buforowany przez ASP.NET Core, więc przewijalny)
    public static async Task<ImageValidationResult> ValidateAsync(IFormFile file, CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return new ImageValidationResult(null, EmptyFileMessage);
        }

        if (file.Length > MaxFileSize)
        {
            return new ImageValidationResult(null, TooLargeMessage);
        }

        await using var content = file.OpenReadStream();
        return await ValidateAsync(content, cancellationToken);
    }

    public static async Task<ImageValidationResult> ValidateAsync(Stream content, CancellationToken cancellationToken)
    {
        var image = await ReadInfoAsync(content, cancellationToken);
        if (image == null)
        {
            return new ImageValidationResult(null, UnsupportedTypeMessage);
        }

        return HasAcceptableDimensions(image.Width, image.Height)
            ? new ImageValidationResult(image, null)
            : new ImageValidationResult(null, DimensionsMessage);
    }

    public static bool HasAcceptableDimensions(int width, int height) =>
        width is > 0 and <= MaxDimension
        && height is > 0 and <= MaxDimension
        && (long)width * height <= MaxPixels;

    // Typ i wymiary obrazu albo null (inny format, uszkodzony albo niepełny nagłówek). Strumień musi być przewijalny;
    // po wywołaniu pozycja wraca na początek.
    public static async Task<ImageFileInfo?> ReadInfoAsync(Stream content, CancellationToken cancellationToken = default)
    {
        if (!content.CanSeek)
        {
            throw new ArgumentException("Image content stream must be seekable", nameof(content));
        }

        try
        {
            content.Position = 0;
            // sygnatura PNG (8 B) + długość i typ chunka IHDR (8 B) + szerokość i wysokość (8 B)
            var header = new byte[24];
            var read = await content.ReadAtLeastAsync(header, header.Length, throwOnEndOfStream: false, cancellationToken);

            if (read >= PngSignature.Length && header.AsSpan(0, PngSignature.Length).SequenceEqual(PngSignature))
            {
                return read == header.Length ? ReadPng(header) : null;
            }

            if (read >= 3 && header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF)
            {
                return await ReadJpegAsync(content, cancellationToken);
            }

            return null;
        }
        finally
        {
            content.Position = 0;
        }
    }

    // PNG: pierwszy chunk musi być IHDR o długości 13; szerokość i wysokość - 4 B big-endian (najwyżej 2^31 - 1)
    private static ImageFileInfo? ReadPng(byte[] header)
    {
        var length = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(8));
        if (length != 13 || !header.AsSpan(12, 4).SequenceEqual("IHDR"u8))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(16));
        var height = BinaryPrimitives.ReadUInt32BigEndian(header.AsSpan(20));
        // wymiar 0 jest niedozwolony w PNG (uszkodzony nagłówek)
        if (width == 0 || height == 0 || width > int.MaxValue || height > int.MaxValue)
        {
            return null;
        }

        return new ImageFileInfo(AttachmentFileType.Png, (int)width, (int)height);
    }

    // JPEG: segmenty od SOI (FF D8) do pierwszego znacznika SOFn (C0-CF poza C4 - DHT, C8 - JPG, CC - DAC), który
    // niesie wysokość i szerokość (2 B big-endian). Początek danych obrazu (SOS) albo koniec pliku przed SOFn - błąd.
    private static async Task<ImageFileInfo?> ReadJpegAsync(Stream content, CancellationToken cancellationToken)
    {
        var buffer = new byte[7];
        content.Position = 2;

        while (true)
        {
            // znacznik: FF, opcjonalne bajty wypełnienia FF, kod znacznika
            if (await ReadByteAsync(content, buffer, cancellationToken) != 0xFF)
            {
                return null;
            }

            int marker;
            do
            {
                marker = await ReadByteAsync(content, buffer, cancellationToken);
            } while (marker == 0xFF);

            switch (marker)
            {
                case < 0:
                case 0x00:
                case 0xD8:
                case 0xD9:
                case 0xDA:
                    return null;
                // znaczniki bez długości (RST0-RST7, TEM)
                case >= 0xD0 and <= 0xD7:
                case 0x01:
                    continue;
            }

            // długość segmentu (z 2 bajtami samej długości)
            if (await content.ReadAtLeastAsync(buffer.AsMemory(0, 2), 2, throwOnEndOfStream: false, cancellationToken) < 2)
            {
                return null;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(buffer);
            if (length < 2)
            {
                return null;
            }

            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                // precyzja (1 B), wysokość (2 B), szerokość (2 B)
                if (length < 7
                    || await content.ReadAtLeastAsync(buffer.AsMemory(0, 5), 5, throwOnEndOfStream: false, cancellationToken) < 5)
                {
                    return null;
                }

                var height = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(1));
                var width = BinaryPrimitives.ReadUInt16BigEndian(buffer.AsSpan(3));
                // wysokość 0 (zdefiniowana dopiero znacznikiem DNL) nie jest obsługiwana
                return width == 0 || height == 0 ? null : new ImageFileInfo(AttachmentFileType.Jpeg, width, height);
            }

            var next = content.Position + length - 2;
            if (next >= content.Length)
            {
                return null;
            }

            content.Position = next;
        }
    }

    // kolejny bajt albo -1 na końcu strumienia
    private static async Task<int> ReadByteAsync(Stream content, byte[] buffer, CancellationToken cancellationToken) =>
        await content.ReadAsync(buffer.AsMemory(0, 1), cancellationToken) == 1 ? buffer[0] : -1;
}
