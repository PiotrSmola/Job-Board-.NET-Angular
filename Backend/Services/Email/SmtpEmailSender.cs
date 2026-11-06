using Backend.Interfaces;
using Backend.Settings;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace Backend.Services.Email;

// Wysyłka przez SMTP (MailKit): osobne połączenie na wiadomość - outbox wysyła wiadomości pojedynczo i rzadko
// (lokalnie Mailpit), a krótkie połączenie nie wymaga utrzymywania i odnawiania sesji
public sealed class SmtpEmailSender : IEmailSender
{
    private readonly EmailSettings _settings;

    public SmtpEmailSender(IOptions<EmailSettings> settings)
    {
        _settings = settings.Value;
    }

    public async Task SendAsync(OutgoingEmail email, CancellationToken cancellationToken = default)
    {
        if (!_settings.IsSmtpConfigured)
        {
            throw new InvalidOperationException("SMTP server is not configured (Email:Smtp:Host)");
        }

        using var message = CreateMessage(email);
        using var client = new SmtpClient();

        var smtp = _settings.Smtp;
        try
        {
            await client.ConnectAsync(smtp.Host!, smtp.Port, smtp.Security, cancellationToken);
            if (!string.IsNullOrEmpty(smtp.UserName))
            {
                await client.AuthenticateAsync(smtp.UserName, smtp.Password ?? string.Empty, cancellationToken);
            }

            await client.SendAsync(message, cancellationToken);
        }
        catch (SmtpCommandException ex) when ((int)ex.StatusCode >= 500 && ex.ErrorCode is SmtpErrorCode.RecipientNotAccepted
                                                   or SmtpErrorCode.SenderNotAccepted or SmtpErrorCode.MessageNotAccepted)
        {
            // trwałe odrzucenie (5xx) - ponawianie nic nie zmieni
            throw new EmailRejectedException($"SMTP server rejected the message ({(int)ex.StatusCode} {ex.ErrorCode})", ex);
        }

        await client.DisconnectAsync(quit: true, cancellationToken);
    }

    // multipart/alternative: wersja tekstowa i HTML; MimeKit koduje nagłówki (temat, nazwy) - znaki CR/LF z danych
    // użytkownika nie dopiszą nagłówków
    private MimeMessage CreateMessage(OutgoingEmail email)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(_settings.FromName, _settings.FromAddress));
        message.To.Add(new MailboxAddress(email.ToName ?? string.Empty, email.ToAddress));
        message.Subject = email.Subject;

        var body = new BodyBuilder { TextBody = email.TextBody, HtmlBody = email.HtmlBody };
        message.Body = body.ToMessageBody();
        return message;
    }
}
