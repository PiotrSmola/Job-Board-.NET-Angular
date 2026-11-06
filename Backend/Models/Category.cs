using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Kategoria ofert zarządzana przez admina; nazwa i slug są unikalne (kolacja bazy - bez wielkości liter)
public class Category
{
    public int Id { get; set; }

    [Required]
    [MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    // fragment adresu / filtr API: małe litery, cyfry i myślniki
    [Required]
    [MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    public int SortOrder { get; set; }

    public ICollection<JobOffer> JobOffers { get; set; } = new List<JobOffer>();
}
