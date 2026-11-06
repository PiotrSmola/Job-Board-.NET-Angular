using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Umiejętność współdzielona między ofertami; dopasowanie po NormalizedName (małe litery, pojedyncze spacje)
public class Skill
{
    public int Id { get; set; }

    // nazwa wyświetlana - taka, jaką wpisano przy pierwszym użyciu
    [Required]
    [MaxLength(50)]
    public string Name { get; set; } = string.Empty;

    // unikalna, w kolacji binarnej - porównanie jest dokładne, bez ukrytych różnic akcentów
    [Required]
    [MaxLength(50)]
    public string NormalizedName { get; set; } = string.Empty;

    public ICollection<JobOffer> JobOffers { get; set; } = new List<JobOffer>();
}
