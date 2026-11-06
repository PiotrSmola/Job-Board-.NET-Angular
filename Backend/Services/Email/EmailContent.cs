namespace Backend.Services.Email;

// Temat i treść wiadomości (HTML i tekst) - wynik szablonu, zapisywany w outboksie
public sealed record EmailContent(string Subject, string HtmlBody, string TextBody);
