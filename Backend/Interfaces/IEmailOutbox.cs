using Backend.Models;

namespace Backend.Interfaces;

// Outbox e-maili: wiadomości trafiają do kontekstu EF i zapisuje je najbliższy SaveChanges - w tej samej transakcji co
// zdarzenie, którego dotyczą (wycofanie transakcji = brak e-maila). Wysyła je w tle EmailOutboxProcessor.
public interface IEmailOutbox
{
    // e-mail z linkiem potwierdzającym adres (zawsze wysyłany - e-mail bezpieczeństwa)
    Task QueueEmailConfirmationAsync(ApplicationUser user);

    // e-mail z linkiem do ustawienia nowego hasła; false - adres dostał już w ostatniej godzinie limit takich e-maili
    // (nic nie dodano)
    Task<bool> QueuePasswordResetAsync(ApplicationUser user);

    // Powiadomienia przygotowane wg preferencji adresatów (pusta lista - nikt ich nie chce); Queue dodaje je do kontekstu.
    // Przygotowanie przed zapisem pozwala dodać je w miejscu, w którym zapis zdarzenia jest już pewny.

    // kandydat - potwierdzenie złożenia aplikacji, członkowie firmy oferty - nowa aplikacja
    Task<IReadOnlyList<EmailMessage>> PrepareApplicationSubmittedAsync(int jobOfferId, string candidateId);

    // kandydat - nowy status aplikacji (z komentarzem rekrutera)
    Task<IReadOnlyList<EmailMessage>> PrepareApplicationStatusChangedAsync(int jobOfferId, string candidateId,
        ApplicationStatus status, string? comment);

    // członkowie firmy - decyzja admina (zatwierdzenie albo odrzucenie z powodem); wymaga aktualnego stanu firmy
    Task<IReadOnlyList<EmailMessage>> PrepareCompanyReviewedAsync(Company company);

    void Queue(IEnumerable<EmailMessage> messages);
}
