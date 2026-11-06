namespace Backend.Interfaces;

// Wysyłka jednej wiadomości (SMTP). Wyjątek = nieudana próba - outbox ponowi wysyłkę później
// (EmailRejectedException - trwałe odrzucenie, bez ponawiania)
public interface IEmailSender
{
    Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default);
}

// Wiadomość do wysłania: adresat, temat i treść w dwóch wersjach (HTML i tekst)
public sealed record OutgoingEmail(string ToAddress, string? ToName, string Subject, string HtmlBody, string TextBody);

// Serwer pocztowy trwale odrzucił wiadomość (np. nieistniejąca skrzynka) - outbox nie ponawia wysyłki
public sealed class EmailRejectedException : Exception
{
    public EmailRejectedException(string message, Exception? innerException = null) : base(message, innerException)
    {
    }
}
