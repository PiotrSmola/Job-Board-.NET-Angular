namespace Backend.DTOs;

// Zdjęcie galerii firmy bez danych magazynu (klucz pliku, typ i rozmiar nie trafiają do API);
// url - GET /api/companies/{companyId}/photos/{id}/file
public class CompanyPhotoDto
{
    public int Id { get; set; }
    public string? Caption { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public string Url { get; set; } = string.Empty;
}
