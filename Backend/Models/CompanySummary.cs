namespace Backend.Models;

// Firma z licznikami wyliczanymi w SQL (projekcja do list i szczegółów - nie jest encją)
public class CompanySummary
{
    public Company Company { get; init; } = null!;

    // oferty aktywne i niewygasłe
    public int ActiveJobsCount { get; init; }

    // konta pracodawców przypisane do firmy
    public int MembersCount { get; init; }
}
