namespace Backend.DTOs;

public class CompanyPhotoOrderDto
{
    // wszystkie zdjęcia firmy, każde dokładnie raz, w nowej kolejności (inaczej 400 "Invalid photo order")
    public List<int>? PhotoIds { get; set; }
}
