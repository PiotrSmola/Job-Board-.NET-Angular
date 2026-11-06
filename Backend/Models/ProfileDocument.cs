using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Dokument w profilu kandydata (np. domyślne CV) - dołączany do aplikacji jako kopia pliku. Klucz pliku w magazynie
// generuje serwer i nigdy nie trafia do DTO; nazwa po sanityzacji, typ i rozmiar ustalone przez serwer
public class ProfileDocument
{
    public int Id { get; set; }

    [Required]
    public string UserId { get; set; } = string.Empty;

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
    public ApplicationUser User { get; set; } = null!;
}
