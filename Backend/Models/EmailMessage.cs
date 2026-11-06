using System.ComponentModel.DataAnnotations;

namespace Backend.Models;

// Wiadomość e-mail w outboksie: zapisywana w tej samej transakcji co zdarzenie (rejestracja, zmiana statusu...),
// wysyłana w tle (EmailOutboxProcessor) z ponawianiem. Treść jest czyszczona po wysłaniu albo ostatecznym błędzie -
// linki z tokenami i dane osobowe nie zostają w bazie dłużej niż to konieczne
public class EmailMessage
{
    public long Id { get; set; }

    // adresat (usunięcie konta usuwa jego wiadomości)
    [Required]
    public string UserId { get; set; } = string.Empty;

    public EmailType Type { get; set; }

    [MaxLength(256)]
    public string ToAddress { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? ToName { get; set; }

    [MaxLength(300)]
    public string Subject { get; set; } = string.Empty;

    // null po wysłaniu albo ostatecznym błędzie
    public string? HtmlBody { get; set; }
    public string? TextBody { get; set; }

    public EmailMessageStatus Status { get; set; } = EmailMessageStatus.Pending;

    // liczba nieudanych i udanych prób wysłania
    public int Attempts { get; set; }

    // UTC
    public DateTime CreatedAt { get; set; }
    public DateTime NextAttemptAt { get; set; }
    public DateTime? SentAt { get; set; }

    // typ i komunikat ostatniego błędu wysyłki (bez danych logowania SMTP)
    [MaxLength(500)]
    public string? LastError { get; set; }

    // Navigation properties
    public ApplicationUser User { get; set; } = null!;
}

public enum EmailMessageStatus
{
    Pending = 0,
    Sent = 1,
    Failed = 2
}

// Rodzaj wiadomości (statystyki, limity i testy) - serializowany jako liczba
public enum EmailType
{
    EmailConfirmation = 0,
    PasswordReset = 1,
    ApplicationSubmitted = 2,
    ApplicationStatusChanged = 3,
    NewApplication = 4,
    CompanyReviewed = 5
}
