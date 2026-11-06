namespace Backend.Models;

// Zapis "ten odwiedzający widział tę ofertę tego dnia (UTC)" - służy wyłącznie deduplikacji wyświetleń.
// Odwiedzający to skrót HMAC (identyfikator użytkownika albo IP + User-Agent), nie surowe IP; wiersze starsze
// niż dzień wstecz usuwa JobViewCleanupService.
public class JobOfferViewer
{
    public const string TableName = "JobOfferViewers";
    public const int VisitorHashLength = 16;

    public int JobOfferId { get; set; }

    public DateOnly Date { get; set; }

    public byte[] VisitorHash { get; set; } = [];

    public JobOffer JobOffer { get; set; } = null!;
}
