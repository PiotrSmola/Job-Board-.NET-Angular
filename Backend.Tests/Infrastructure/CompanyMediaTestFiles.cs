using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using Backend.DTOs;
using Backend.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Backend.Tests.Infrastructure;

// Obrazy z poprawnymi nagłówkami (PNG z IHDR, JPEG z segmentem SOFn) o zadanych wymiarach oraz żądania galerii
// zdjęć i tła ofert firmy
public static class CompanyMediaTestFiles
{
    public const string PhotosUrl = "/api/employer/company/photos";
    public const string PhotoOrderUrl = "/api/employer/company/photos/order";
    public const string BackgroundUrl = "/api/employer/company/offer-background";

    public static string PhotoUrl(int photoId) => $"{PhotosUrl}/{photoId}";

    public static string PublicPhotosUrl(int companyId) => $"/api/companies/{companyId}/photos";

    public static string PublicPhotoFileUrl(int companyId, int photoId) => $"/api/companies/{companyId}/photos/{photoId}/file";

    public static string PublicBackgroundFileUrl(int companyId) => $"/api/companies/{companyId}/offer-background/file";

    public static string AdminPhotoUrl(int companyId, int photoId) => $"/api/admin/companies/{companyId}/photos/{photoId}";

    public static string AdminBackgroundUrl(int companyId) => $"/api/admin/companies/{companyId}/offer-background";

    // sygnatura, chunk IHDR (szerokość, wysokość, 8 bitów RGB, suma CRC bez znaczenia - serwer nie dekoduje obrazu)
    // i opcjonalne wypełnienie (np. do testu limitu rozmiaru)
    public static byte[] Png(int width = 640, int height = 480, int paddingBytes = 0)
    {
        var bytes = new byte[8 + 25 + paddingBytes];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 13);
        "IHDR"u8.CopyTo(bytes.AsSpan(12));
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20), (uint)height);
        bytes[24] = 8;
        bytes[25] = 2;
        return bytes;
    }

    // SOI, APP0 (JFIF), opcjonalny APP1 (np. EXIF) przed SOFn, SOF0 (albo SOF2 - progresywny), początek SOS i EOI
    public static byte[] Jpeg(int width = 800, int height = 600, bool progressive = false, int app1Bytes = 0, bool fillBytes = false)
    {
        using var stream = new MemoryStream();
        stream.Write([0xFF, 0xD8]);
        stream.Write([0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x00, 0x00, 0x01, 0x00, 0x01, 0x00, 0x00]);
        if (app1Bytes > 0)
        {
            var length = app1Bytes + 2;
            stream.Write([0xFF, 0xE1, (byte)(length >> 8), (byte)length]);
            stream.Write(new byte[app1Bytes]);
        }

        if (fillBytes)
        {
            // bajty wypełnienia FF przed znacznikiem są dozwolone
            stream.Write([0xFF, 0xFF]);
        }

        stream.Write([0xFF, progressive ? (byte)0xC2 : (byte)0xC0, 0x00, 0x11, 0x08,
            (byte)(height >> 8), (byte)height, (byte)(width >> 8), (byte)width,
            0x03, 0x01, 0x22, 0x00, 0x02, 0x11, 0x01, 0x03, 0x11, 0x01]);
        stream.Write([0xFF, 0xDA, 0x00, 0x08, 0x01, 0x01, 0x00, 0x00, 0x3F, 0x00, 0x12, 0x34]);
        stream.Write([0xFF, 0xD9]);
        return stream.ToArray();
    }

    // Formularz z plikami w polu fieldName (zdjęcia - "files", tło - "file")
    public static MultipartFormDataContent Form(string fieldName, params byte[][] files)
    {
        var form = new MultipartFormDataContent();
        for (var i = 0; i < files.Length; i++)
        {
            var content = new ByteArrayContent(files[i]);
            content.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(content, fieldName, $"image-{i}.png");
        }

        return form;
    }

    public static Task<HttpResponseMessage> UploadPhotosAsync(this HttpClient client, params byte[][] files) =>
        client.PostAsync(PhotosUrl, Form("files", files));

    public static Task<HttpResponseMessage> UploadBackgroundAsync(this HttpClient client, byte[] file) =>
        client.PutAsync(BackgroundUrl, Form("file", file));

    // Upload z oczekiwanym sukcesem; zwraca całą galerię
    public static async Task<List<CompanyPhotoDto>> UploadPhotosOkAsync(this HttpClient client, params byte[][] files)
    {
        var response = await client.UploadPhotosAsync(files);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return await response.ReadJsonAsync<List<CompanyPhotoDto>>();
    }

    public static async Task<OfferBackgroundDto> UploadBackgroundOkAsync(this HttpClient client, byte[] file)
    {
        var response = await client.UploadBackgroundAsync(file);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return await response.ReadJsonAsync<OfferBackgroundDto>();
    }

    // Nagłówek Cache-Control dokładnie tak, jak wysłał go serwer (sparsowany CacheControlHeaderValue zmienia kolejność)
    public static string RawCacheControl(this HttpResponseMessage response) =>
        response.Headers.NonValidated.TryGetValues("Cache-Control", out var values) ? values.ToString() : string.Empty;

    // Czy plik istnieje w magazynie aplikacji (np. po usunięciu rekordu)
    public static async Task<bool> FileExistsAsync(this ApiFixture api, string key)
    {
        var storage = api.Factory.Services.GetRequiredService<IFileStorage>();
        await using var file = await storage.OpenReadAsync(key);
        return file != null;
    }

    // Klucze plików zdjęć firmy z bazy (API ich nie zwraca)
    public static Task<List<string>> PhotoKeysAsync(this ApiFixture api, int companyId) =>
        api.QueryDbAsync(db =>
            db.CompanyPhotos.Where(p => p.CompanyId == companyId).OrderBy(p => p.SortOrder).Select(p => p.FileKey).ToListAsync());

    public static Task<string?> BackgroundKeyAsync(this ApiFixture api, int companyId) =>
        api.QueryDbAsync(db =>
            db.Companies.Where(c => c.Id == companyId).Select(c => c.OfferBackgroundKey).FirstOrDefaultAsync());
}
