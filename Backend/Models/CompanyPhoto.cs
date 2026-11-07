using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Zdjęcie w galerii profilu firmy. Klucz pliku w magazynie generuje serwer i nigdy nie trafia do DTO; typ, rozmiar
// i wymiary ustalone przez serwer z zawartości pliku
public class CompanyPhoto
{
    public int Id { get; set; }

    public int CompanyId { get; set; }

    [MaxLength(300)]
    public string FileKey { get; set; } = string.Empty;

    [MaxLength(100)]
    public string ContentType { get; set; } = string.Empty;

    public long Size { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }

    [MaxLength(150)]
    public string? Caption { get; set; }

    // kolejność w galerii (0 = pierwsze, duże zdjęcie)
    public int SortOrder { get; set; }

    // UTC
    public DateTime UploadedAt { get; set; }

    // Navigation properties
    public Company Company { get; set; } = null!;
}
