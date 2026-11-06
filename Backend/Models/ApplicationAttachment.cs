using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Plik dołączony do aplikacji (CV, list motywacyjny, portfolio, inny). Klucz pliku w magazynie (IFileStorage)
// generuje serwer i nigdy nie trafia do DTO; nazwa po sanityzacji, typ i rozmiar ustalone przez serwer
public class ApplicationAttachment
{
    public int Id { get; set; }

    public int JobApplicationId { get; set; }

    public AttachmentKind Kind { get; set; }

    [MaxLength(300)]
    public string FileKey { get; set; } = string.Empty;

    [MaxLength(255)]
    public string FileName { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    // UTC
    public DateTime UploadedAt { get; set; }

    // Navigation properties
    public JobApplication JobApplication { get; set; } = null!;
}
