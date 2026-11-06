using Backend.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Helpers;

public static class FileDownloads
{
    // Odpowiedź z plikiem z magazynu (załącznik aplikacji, dokument profilu): pobranie zamiast wyświetlenia
    // (Content-Disposition attachment), bez zgadywania typu przez przeglądarkę i bez cache
    public static async Task<IActionResult> StoredFileAsync(this ControllerBase controller, StoredFile file, string fileName,
        string contentType)
    {
        try
        {
            var response = controller.Response;
            response.Headers.ContentDisposition = AttachmentFiles.ContentDisposition(fileName);
            response.Headers.XContentTypeOptions = "nosniff";
            response.Headers.CacheControl = "no-store";
            // rozmiar podany przez magazyn (Local - plik, S3 - nagłówek odpowiedzi); strumienia S3 nie da się
            // przewijać, więc FileStreamResult sam by go nie ustawił
            if (file.Length.HasValue)
            {
                response.ContentLength = file.Length.Value;
            }

            // FileStreamResult zwalnia strumień po wysłaniu odpowiedzi (także przerwanej)
            return controller.File(file.Content, contentType);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
    }
}
