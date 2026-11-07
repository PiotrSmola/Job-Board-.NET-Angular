using System.ComponentModel.DataAnnotations;
using Backend.Helpers;
using Backend.Models;

namespace Backend.DTOs;

// Ustawienia tła ofert; pole pominięte (null) - bez zmian
public class UpdateOfferBackgroundDto
{
    [Range(CompanyMedia.MinBackgroundDim, CompanyMedia.MaxBackgroundDim)]
    public int? Dim { get; set; }

    // System.Text.Json przyjmuje dowolną liczbę dla enuma - EnumDataType odrzuca wartości spoza OfferBackgroundFocus
    [EnumDataType(typeof(OfferBackgroundFocus))]
    public OfferBackgroundFocus? Focus { get; set; }
}
