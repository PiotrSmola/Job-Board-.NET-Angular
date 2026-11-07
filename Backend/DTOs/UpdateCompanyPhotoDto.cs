using System.ComponentModel.DataAnnotations;
using Backend.Helpers;

namespace Backend.DTOs;

public class UpdateCompanyPhotoDto
{
    // pusty albo null - zdjęcie bez podpisu
    [MaxLength(CompanyMedia.MaxCaptionLength)]
    public string? Caption { get; set; }
}
