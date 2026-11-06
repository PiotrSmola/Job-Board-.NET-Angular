using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;
using Backend.Models;

namespace Backend.Tests.Infrastructure;

// Plik do wysłania w jednym żądaniu uploadu; Kind = null - bez pola "kinds" dla tego pliku
public sealed record AttachmentUpload(byte[] Content, string FileName, AttachmentKind? Kind = null,
    string ContentType = AttachmentTestFiles.PdfContentType);

// Przykładowe pliki załączników (PDF, DOCX, PNG, JPEG i podróbki) oraz żądania
// POST/GET/DELETE /api/jobapplications/{id}/attachments
public static class AttachmentTestFiles
{
    public const string PdfContentType = "application/pdf";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";
    public const string PngContentType = "image/png";
    public const string JpegContentType = "image/jpeg";

    public static string Url(int applicationId) => $"/api/jobapplications/{applicationId}/attachments";

    public static string Url(int applicationId, int attachmentId) => $"{Url(applicationId)}/{attachmentId}";

    public static byte[] Pdf(int paddingBytes = 0)
    {
        var builder = new StringBuilder("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n");
        builder.Append('%', paddingBytes);
        builder.Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    // PDF o dokładnie zadanym rozmiarze
    public static byte[] PdfOfSize(long size) => Pdf(paddingBytes: (int)size - Pdf().Length);

    // minimalny pakiet OOXML: [Content_Types].xml i word/document.xml
    public static byte[] Docx() => Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>"));

    // Stała data wpisów ZIP: ta sama treść daje zawsze te same bajty. Domyślnie wpis dostaje bieżący czas zapisany
    // w nagłówku z dokładnością do 2 s - plik zbudowany ponownie po chwili (np. oczekiwana treść pobranego załącznika)
    // różnił się wtedy bajtem czasu i porównanie zawodziło losowo.
    public static readonly DateTimeOffset ZipEntryTime = new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    // ZIP bez części dokumentu Word (np. zwykłe archiwum albo XLSX)
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                entry.LastWriteTime = ZipEntryTime;
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    // sygnatura PNG i początek nagłówka IHDR (serwer sprawdza tylko sygnaturę)
    public static byte[] Png(int paddingBytes = 0) =>
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52, .. new byte[paddingBytes]];

    // nagłówek JPEG (SOI + APP0 JFIF)
    public static byte[] Jpeg(int paddingBytes = 0) =>
        [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46, 0x49, 0x46, 0x00, .. new byte[paddingBytes]];

    // GIF - obraz w formacie spoza listy dozwolonych
    public static byte[] Gif() => "GIF89a\x01\x00\x01\x00"u8.ToArray();

    // Formularz z plikami w polach "files" i (gdy podano rodzaj) równoległymi polami "kinds"
    public static MultipartFormDataContent Form(params AttachmentUpload[] uploads) => Form("files", uploads);

    public static MultipartFormDataContent Form(string fieldName, params AttachmentUpload[] uploads)
    {
        var form = new MultipartFormDataContent();
        foreach (var upload in uploads)
        {
            var file = new ByteArrayContent(upload.Content);
            file.Headers.ContentType = new MediaTypeHeaderValue(upload.ContentType);
            form.Add(file, fieldName, upload.FileName);
        }

        foreach (var upload in uploads.Where(u => u.Kind.HasValue))
        {
            form.Add(new StringContent(((int)upload.Kind!.Value).ToString()), "kinds");
        }

        return form;
    }

    // Ręcznie złożony multipart z jednym plikiem i dowolnymi parametrami Content-Disposition części (np. filename*
    // z zakodowanymi CR/LF i cudzysłowem, których HttpClient nie pozwala ustawić w nazwie pliku)
    public static HttpContent RawForm(byte[] content, string dispositionParameters)
    {
        const string boundary = "attachment-test-boundary";
        using var body = new MemoryStream();
        var head = $"--{boundary}\r\nContent-Disposition: form-data; name=\"files\"; {dispositionParameters}\r\n" +
                   $"Content-Type: {PdfContentType}\r\n\r\n";
        body.Write(Encoding.ASCII.GetBytes(head));
        body.Write(content);
        body.Write(Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n"));

        var result = new ByteArrayContent(body.ToArray());
        result.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return result;
    }

    public static Task<HttpResponseMessage> UploadAttachmentsAsync(this HttpClient client, int applicationId,
        params AttachmentUpload[] uploads) =>
        client.PostAsync(Url(applicationId), Form(uploads));

    public static Task<HttpResponseMessage> UploadAttachmentAsync(this HttpClient client, int applicationId, byte[] content,
        string fileName = "cv.pdf", string contentType = PdfContentType, AttachmentKind? kind = null) =>
        client.UploadAttachmentsAsync(applicationId, new AttachmentUpload(content, fileName, kind, contentType));

    public static Task<HttpResponseMessage> DownloadAttachmentAsync(this HttpClient client, int applicationId, int attachmentId) =>
        client.GetAsync(Url(applicationId, attachmentId));

    public static Task<HttpResponseMessage> DeleteAttachmentAsync(this HttpClient client, int applicationId, int attachmentId) =>
        client.DeleteAsync(Url(applicationId, attachmentId));
}
