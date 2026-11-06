using System.Net;
using System.Text;
using Backend.Models;
using Backend.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Services.Email;

// Szablony e-maili (po polsku, HTML i tekst). Dane użytkowników (imiona, tytuły ofert, komentarze) są kodowane w HTML;
// linki prowadzą do frontendu (Frontend:BaseUrl). Wiadomości zawierają tylko dane potrzebne adresatowi - bez adresów
// e-mail i danych innych osób (np. pracodawca nie dostaje danych kandydata, tylko informację o nowej aplikacji).
public sealed class EmailTemplates
{
    public const int MaxSubjectLength = 300;

    private readonly string _baseUrl;

    public EmailTemplates(IOptions<FrontendSettings> frontend)
    {
        _baseUrl = frontend.Value.BaseUrl.TrimEnd('/');
    }

    public string ConfirmEmailUrl(string userId, string token) =>
        $"{_baseUrl}/confirm-email?userId={Uri.EscapeDataString(userId)}&token={EmailTokens.Encode(token)}";

    public string ResetPasswordUrl(string email, string token) =>
        $"{_baseUrl}/reset-password?email={Uri.EscapeDataString(email)}&token={EmailTokens.Encode(token)}";

    private string MyApplicationsUrl => $"{_baseUrl}/my-applications";
    private string EmployerPanelUrl => $"{_baseUrl}/employer";
    private string NotificationSettingsUrl => $"{_baseUrl}/account?tab=notifications";

    public EmailContent EmailConfirmation(string? firstName, string userId, string token, TimeSpan validFor) =>
        Security("Potwierdź adres e-mail w Job Board", firstName)
            .Paragraph("Dziękujemy za założenie konta w Job Board. Potwierdź, że ten adres e-mail należy do Ciebie.")
            .Button("Potwierdź adres e-mail", ConfirmEmailUrl(userId, token))
            .Note($"Link jest ważny przez {Describe(validFor)}. Jeśli to nie Ty zakładałeś konto, zignoruj tę wiadomość.")
            .Build();

    public EmailContent PasswordReset(string? firstName, string email, string token, TimeSpan validFor) =>
        Security("Zmiana hasła w Job Board", firstName)
            .Paragraph("Otrzymaliśmy prośbę o ustawienie nowego hasła do Twojego konta w Job Board.")
            .Button("Ustaw nowe hasło", ResetPasswordUrl(email, token))
            .Note($"Link jest ważny przez {Describe(validFor)} i można go użyć tylko raz. Jeśli to nie Ty prosiłeś o zmianę " +
                  "hasła, zignoruj tę wiadomość – Twoje hasło pozostanie bez zmian.")
            .Build();

    public EmailContent ApplicationSubmitted(string? firstName, string jobTitle, string companyName) =>
        Notification($"Aplikacja złożona: {jobTitle}", firstName)
            .Paragraph($"Twoja aplikacja na stanowisko „{jobTitle}” w firmie {companyName} została złożona.")
            .Paragraph("Jej status możesz śledzić w zakładce Moje aplikacje – o zmianach poinformujemy Cię e-mailem.")
            .Button("Moje aplikacje", MyApplicationsUrl)
            .Build();

    public EmailContent ApplicationStatusChanged(string? firstName, string jobTitle, string companyName,
        ApplicationStatus status, string? comment)
    {
        var builder = Notification($"Zmiana statusu aplikacji: {jobTitle}", firstName)
            .Paragraph($"Status Twojej aplikacji na stanowisko „{jobTitle}” w firmie {companyName} zmienił się na: {StatusLabel(status)}.");
        if (!string.IsNullOrWhiteSpace(comment))
        {
            builder.Quote("Komentarz rekrutera", comment);
        }

        return builder.Button("Moje aplikacje", MyApplicationsUrl).Build();
    }

    public EmailContent NewApplication(string? firstName, string jobTitle) =>
        Notification($"Nowa aplikacja: {jobTitle}", firstName)
            .Paragraph($"Na ofertę „{jobTitle}” wpłynęła nowa aplikacja.")
            .Paragraph("Szczegóły aplikacji i załączniki kandydata znajdziesz w panelu pracodawcy.")
            .Button("Panel pracodawcy", EmployerPanelUrl)
            .Build();

    public EmailContent CompanyReviewed(string? firstName, string companyName, bool approved, string? reason)
    {
        if (approved)
        {
            return Notification($"Firma {companyName} została zatwierdzona", firstName)
                .Paragraph($"Administrator zatwierdził firmę {companyName}. Możesz teraz publikować oferty pracy.")
                .Button("Panel pracodawcy", EmployerPanelUrl)
                .Build();
        }

        var builder = Notification($"Firma {companyName} nie została zatwierdzona", firstName)
            .Paragraph($"Administrator odrzucił zgłoszenie firmy {companyName}.");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            builder.Quote("Powód", reason);
        }

        return builder
            .Paragraph("Popraw dane firmy w panelu pracodawcy – zmiana zgłosi ją ponownie do weryfikacji.")
            .Button("Panel pracodawcy", EmployerPanelUrl)
            .Build();
    }

    // te same etykiety co we frontendzie (status-labels.ts)
    public static string StatusLabel(ApplicationStatus status) => status switch
    {
        ApplicationStatus.Submitted => "Złożona",
        ApplicationStatus.InReview => "W analizie",
        ApplicationStatus.Interview => "Rozmowa",
        ApplicationStatus.Offered => "Oferta",
        ApplicationStatus.Hired => "Zatrudniony",
        ApplicationStatus.Rejected => "Odrzucona",
        ApplicationStatus.Withdrawn => "Wycofana",
        _ => "Nieznany"
    };

    // czas ważności linku: "1 godzinę", "12 godz.", "1 dzień", "3 dni"
    public static string Describe(TimeSpan validFor)
    {
        if (validFor < TimeSpan.FromHours(1))
        {
            return $"{Math.Max(1, (int)validFor.TotalMinutes)} min";
        }

        if (validFor < TimeSpan.FromDays(1))
        {
            var hours = (int)validFor.TotalHours;
            return hours == 1 ? "1 godzinę" : $"{hours} godz.";
        }

        var days = (int)validFor.TotalDays;
        return days == 1 ? "1 dzień" : $"{days} dni";
    }

    private static Builder Security(string subject, string? firstName) =>
        new(subject, firstName, "Ta wiadomość została wysłana automatycznie – nie odpowiadaj na nią.", null);

    private Builder Notification(string subject, string? firstName) =>
        new(subject, firstName,
            "Otrzymujesz tę wiadomość, ponieważ masz włączone powiadomienia e-mail w Job Board. Możesz je wyłączyć w ustawieniach konta.",
            ("Ustawienia powiadomień", NotificationSettingsUrl));

    // Składa wiadomość z akapitów (tekst użytkownika zawsze jako zwykły tekst - kodowany w HTML)
    private sealed class Builder
    {
        private const string TextColor = "#1f2933";
        private const string MutedColor = "#52606d";
        // biały tekst na #00695c - kontrast 6,3:1; #52606d na białym - 6,4:1
        private const string AccentColor = "#00695c";

        private readonly string _subject;
        private readonly string _footer;
        private readonly (string Label, string Url)? _footerLink;
        private readonly StringBuilder _html = new();
        private readonly StringBuilder _text = new();

        public Builder(string subject, string? firstName, string footer, (string Label, string Url)? footerLink)
        {
            _subject = Subject(subject);
            _footer = footer;
            _footerLink = footerLink;

            var name = SingleLine(firstName);
            Paragraph(name.Length == 0 ? "Cześć," : $"Cześć {name},");
        }

        public Builder Paragraph(string text)
        {
            _html.Append($"<p style=\"margin:0 0 16px\">{Encode(text)}</p>");
            _text.Append(text).Append("\n\n");
            return this;
        }

        public Builder Quote(string label, string text)
        {
            var trimmed = text.Trim().Replace("\r\n", "\n");
            _html.Append($"<p style=\"margin:0 0 4px;color:{MutedColor}\">{Encode(label)}:</p>")
                .Append($"<blockquote style=\"margin:0 0 16px;padding:8px 12px;border-left:4px solid {AccentColor};background:#f0f4f4\">")
                .Append(Encode(trimmed))
                .Append("</blockquote>");
            _text.Append(label).Append(":\n");
            foreach (var line in trimmed.Split('\n'))
            {
                _text.Append("> ").Append(line).Append('\n');
            }

            _text.Append('\n');
            return this;
        }

        public Builder Button(string label, string url)
        {
            var href = WebUtility.HtmlEncode(url);
            _html.Append("<p style=\"margin:24px 0\">")
                .Append($"<a href=\"{href}\" style=\"display:inline-block;padding:12px 20px;background:{AccentColor};color:#ffffff;")
                .Append($"border-radius:4px;font-weight:bold;text-decoration:none\">{Encode(label)}</a></p>")
                .Append($"<p style=\"margin:0 0 16px;font-size:13px;color:{MutedColor}\">Jeśli przycisk nie działa, skopiuj ten adres do przeglądarki:<br>")
                .Append($"<a href=\"{href}\" style=\"color:{AccentColor};word-break:break-all\">{href}</a></p>");
            _text.Append(label).Append(": ").Append(url).Append("\n\n");
            return this;
        }

        public Builder Note(string text)
        {
            _html.Append($"<p style=\"margin:0 0 16px;font-size:13px;color:{MutedColor}\">{Encode(text)}</p>");
            _text.Append(text).Append("\n\n");
            return this;
        }

        public EmailContent Build()
        {
            var footerHtml = Encode(_footer);
            var footerText = _footer;
            if (_footerLink is { } link)
            {
                var href = WebUtility.HtmlEncode(link.Url);
                footerHtml += $" <a href=\"{href}\" style=\"color:{MutedColor}\">{Encode(link.Label)}</a>";
                footerText += $"\n{link.Label}: {link.Url}";
            }

            var html = new StringBuilder()
                .Append("<!DOCTYPE html><html lang=\"pl\"><head><meta charset=\"utf-8\">")
                .Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">")
                .Append($"<title>{Encode(_subject)}</title></head>")
                .Append($"<body style=\"margin:0;padding:24px 16px;background:#f5f7fa;color:{TextColor};")
                .Append("font-family:Arial,Helvetica,sans-serif;font-size:16px;line-height:1.5\">")
                .Append("<div style=\"max-width:600px;margin:0 auto;padding:24px;background:#ffffff;border-radius:8px\">")
                .Append($"<h1 style=\"margin:0 0 24px;font-size:20px\">{Encode(_subject)}</h1>")
                .Append(_html)
                .Append("<hr style=\"border:none;border-top:1px solid #d9e2ec;margin:24px 0 16px\">")
                .Append($"<p style=\"margin:0;font-size:13px;color:{MutedColor}\">{footerHtml}</p>")
                .Append("</div></body></html>")
                .ToString();

            return new EmailContent(_subject, html, $"{_text}--\n{footerText}\n");
        }

        // HTML z zachowaniem podziału wierszy (np. komentarz rekrutera)
        private static string Encode(string text) =>
            WebUtility.HtmlEncode(text).Replace("\r\n", "\n").Replace("\n", "<br>");

        // temat w jednym wierszu (bez znaków sterujących z tytułów ofert i nazw firm), najwyżej 300 znaków
        private static string Subject(string subject)
        {
            var line = SingleLine(subject);
            return line.Length <= MaxSubjectLength ? line : line[..(MaxSubjectLength - 1)].TrimEnd() + "…";
        }

        private static string SingleLine(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            var builder = new StringBuilder(value.Length);
            foreach (var c in value)
            {
                builder.Append(char.IsControl(c) ? ' ' : c);
            }

            return string.Join(' ', builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }
    }
}
