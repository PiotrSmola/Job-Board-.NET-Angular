namespace Backend.Models;

// Firma z licznikami wyliczanymi w SQL (projekcja do list i szczegółów - nie jest encją)
public class CompanySummary
{
    public Company Company { get; init; } = null!;

    // oferty widoczne publicznie: aktywne i niewygasłe, tylko w firmie zatwierdzonej (inaczej 0)
    public int ActiveJobsCount { get; init; }

    // konta pracodawców przypisane do firmy
    public int MembersCount { get; init; }

    // zdjęcia w galerii firmy
    public int PhotosCount { get; init; }
}
