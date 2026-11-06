using System.IO.Compression;
using System.Net.Http.Headers;
using System.Text;

namespace Backend.Tests.Infrastructure;

// Przykładowe pliki CV (PDF, DOCX i podróbki) oraz upload przez PUT /api/jobapplications/{id}/cv
public static class CvTestFiles
{
    public const string PdfContentType = "application/pdf";
    public const string DocxContentType = "application/vnd.openxmlformats-officedocument.wordprocessingml.document";

    public static byte[] Pdf(int paddingBytes = 0)
    {
        var builder = new StringBuilder("%PDF-1.4\n1 0 obj << /Type /Catalog >> endobj\n");
        builder.Append('%', paddingBytes);
        builder.Append("\n%%EOF\n");
        return Encoding.ASCII.GetBytes(builder.ToString());
    }

    // minimalny pakiet OOXML: [Content_Types].xml i word/document.xml
    public static byte[] Docx() => Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>"));

    // ZIP bez części dokumentu Word (np. zwykłe archiwum albo XLSX)
    public static byte[] Zip(params (string Name, string Content)[] entries)
    {
        using var buffer = new MemoryStream();
        using (var archive = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var writer = new StreamWriter(archive.CreateEntry(name).Open());
                writer.Write(content);
            }
        }

        return buffer.ToArray();
    }

    // nagłówek PNG - np. obraz przemianowany na .pdf
    public static byte[] Png() => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52];

    public static MultipartFormDataContent Form(byte[] content, string fileName, string contentType = PdfContentType, string fieldName = "file")
    {
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);

        var form = new MultipartFormDataContent();
        form.Add(file, fieldName, fileName);
        return form;
    }

    // Ręcznie złożony multipart z dowolnymi parametrami Content-Disposition części (np. filename* z zakodowanymi
    // CR/LF i cudzysłowem, których HttpClient nie pozwala ustawić w nazwie pliku)
    public static HttpContent RawForm(byte[] content, string dispositionParameters)
    {
        const string boundary = "cv-test-boundary";
        using var body = new MemoryStream();
        var head = $"--{boundary}\r\nContent-Disposition: form-data; name=\"file\"; {dispositionParameters}\r\n" +
                   $"Content-Type: {PdfContentType}\r\n\r\n";
        body.Write(Encoding.ASCII.GetBytes(head));
        body.Write(content);
        body.Write(Encoding.ASCII.GetBytes($"\r\n--{boundary}--\r\n"));

        var result = new ByteArrayContent(body.ToArray());
        result.Headers.ContentType = MediaTypeHeaderValue.Parse($"multipart/form-data; boundary={boundary}");
        return result;
    }

    public static Task<HttpResponseMessage> UploadCvAsync(this HttpClient client, int applicationId, byte[] content,
        string fileName = "cv.pdf", string contentType = PdfContentType) =>
        client.PutAsync($"/api/jobapplications/{applicationId}/cv", Form(content, fileName, contentType));
}
