using System.ComponentModel.DataAnnotations;

namespace Backend.DTOs;

// Body POST/PUT /api/admin/categories
public class CategoryFormDto
{
    // pusta nazwa po normalizacji (same białe lub niewidoczne znaki) jest odrzucana przez kontroler
    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    // \z zamiast $ - $ dopuszcza końcowy znak nowej linii
    [Required]
    [MaxLength(100)]
    [RegularExpression(@"^[a-z0-9-]+\z", ErrorMessage = "Slug may contain only lowercase letters, digits and hyphens")]
    public string Slug { get; set; } = string.Empty;

    [Range(0, 100000)]
    public int SortOrder { get; set; }
}
