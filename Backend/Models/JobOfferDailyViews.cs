namespace Backend.Models;

// Dzienny agregat wyświetleń szczegółów oferty (dzień UTC); zwiększany przez JobViewTracker
public class JobOfferDailyViews
{
    public const string TableName = "JobOfferDailyViews";

    public int JobOfferId { get; set; }

    public DateOnly Date { get; set; }

    public int Views { get; set; }

    public JobOffer JobOffer { get; set; } = null!;
}
