using Backend.Models;

namespace Backend.DTOs;

// Tło nagłówka ofert firmy: adres obrazu (z wersją - zmienia się razem z obrazem), przyciemnienie w procentach
// (55-95) i punkt zaczepienia. Url = null tylko w odpowiedzi PATCH, gdy firma nie ma jeszcze obrazu tła
public class OfferBackgroundDto
{
    public string? Url { get; set; }
    public int Dim { get; set; }
    public OfferBackgroundFocus Focus { get; set; }
}
