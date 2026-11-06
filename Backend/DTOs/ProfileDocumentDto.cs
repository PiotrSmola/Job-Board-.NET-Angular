using Backend.Models;

namespace Backend.DTOs;

// Dokument profilu bez danych magazynu (klucz pliku i content type nie trafiają do API)
public class ProfileDocumentDto
{
    public int Id { get; set; }
    public AttachmentKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UploadedAt { get; set; }
}
