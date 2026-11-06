using Backend.Models;

namespace Backend.DTOs;

// Załącznik aplikacji bez danych magazynu (klucz pliku i content type nie trafiają do API)
public class ApplicationAttachmentDto
{
    public int Id { get; set; }
    public AttachmentKind Kind { get; set; }
    public string FileName { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime UploadedAt { get; set; }
}
