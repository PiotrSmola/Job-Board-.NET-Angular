using System.Web;
using Backend.Models;
using Backend.Services.Email;
using Backend.Settings;
using Microsoft.Extensions.Options;

namespace Backend.Tests.Unit;

// Szablony e-maili: linki do frontendu z tokenami Base64Url, kodowanie danych użytkownika w HTML, temat w jednym
// wierszu, wersja tekstowa z tymi samymi linkami
public class EmailTemplatesTests
{
    private const string BaseUrl = "https://jobs.example.test:8443";

    // token Identity zawiera znaki, które w zapytaniu URL psują się bez kodowania ("+" -> spacja, "/" i "=")
    private const string Token = "CfDJ8+abc/def==xyz+";

    private static readonly EmailTemplates Templates = new(Options.Create(new FrontendSettings { BaseUrl = BaseUrl }));

    [Fact]
    public void ConfirmEmailUrl_EncodesTheTokenAsBase64UrlAndTheUserId()
    {
        var url = new Uri(Templates.ConfirmEmailUrl("user id/1", Token));

        Assert.Equal($"{BaseUrl}/confirm-email", url.GetLeftPart(UriPartial.Path));
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("user id/1", query["userId"]);
        var encoded = query["token"]!;
        Assert.Matches("^[A-Za-z0-9_-]+$", encoded);
        Assert.True(EmailTokens.TryDecode(encoded, out var decoded));
        Assert.Equal(Token, decoded);
    }

    [Fact]
    public void ResetPasswordUrl_EncodesTheEmailAndTheToken()
    {
        var url = new Uri(Templates.ResetPasswordUrl("jan+test@example.com", Token));

        Assert.Equal($"{BaseUrl}/reset-password", url.GetLeftPart(UriPartial.Path));
        Assert.Contains("email=jan%2Btest%40example.com", url.Query);
        var query = HttpUtility.ParseQueryString(url.Query);
        Assert.Equal("jan+test@example.com", query["email"]);
        Assert.True(EmailTokens.TryDecode(query["token"], out var decoded));
        Assert.Equal(Token, decoded);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not base64!")]
    [InlineData("%%%")]
    [InlineData("A")]
    public void TryDecode_RejectsMissingOrMalformedTokens(string? encoded)
    {
        Assert.False(EmailTokens.TryDecode(encoded, out _));
    }

    [Fact]
    public void TryDecode_RejectsTokensLongerThan2048Characters()
    {
        var encoded = EmailTokens.Encode(new string('a', 2000));
        Assert.True(encoded.Length > EmailTokens.MaxEncodedLength);

        Assert.False(EmailTokens.TryDecode(encoded, out _));
    }

    [Fact]
    public void TryDecode_RejectsBytesThatAreNotUtf8()
    {
        Assert.False(EmailTokens.TryDecode(Microsoft.AspNetCore.WebUtilities.WebEncoders.Base64UrlEncode([0xFF, 0xFE, 0xFD]), out _));
    }

    [Fact]
    public void EmailConfirmation_ContainsTheLinkInBothVersionsAndTheValidity()
    {
        var email = Templates.EmailConfirmation("Jan", "u1", Token, TimeSpan.FromDays(3));

        var url = Templates.ConfirmEmailUrl("u1", Token);
        Assert.Equal("Potwierdź adres e-mail w Job Board", email.Subject);
        Assert.Contains(url, email.TextBody);
        // w HTML "&" w adresie jest kodowane
        Assert.Contains($"href=\"{url.Replace("&", "&amp;")}\"", email.HtmlBody);
        Assert.Contains("Cześć Jan,", email.TextBody);
        Assert.Contains("3 dni", email.TextBody);
        Assert.Contains("<html lang=\"pl\">", email.HtmlBody);
    }

    [Fact]
    public void PasswordReset_SaysTheLinkIsValidForOneHourAndCanBeUsedOnce()
    {
        var email = Templates.PasswordReset("Jan", "jan@example.com", Token, TimeSpan.FromHours(1));

        Assert.Equal("Zmiana hasła w Job Board", email.Subject);
        Assert.Contains(Templates.ResetPasswordUrl("jan@example.com", Token), email.TextBody);
        Assert.Contains("1 godzinę", email.TextBody);
        Assert.Contains("tylko raz", email.TextBody);
        // e-mail bezpieczeństwa - bez stopki o powiadomieniach
        Assert.DoesNotContain("/account?tab=notifications", email.TextBody);
    }

    [Fact]
    public void UserContent_IsHtmlEncoded()
    {
        var email = Templates.ApplicationStatusChanged("<b>Jan</b>", "Dev <script>alert(1)</script>", "A&B \"Co\"",
            ApplicationStatus.Interview, "Zapraszamy <img src=x onerror=alert(1)>\ndruga linia");

        Assert.DoesNotContain("<script>", email.HtmlBody);
        Assert.DoesNotContain("<img", email.HtmlBody);
        Assert.DoesNotContain("<b>Jan", email.HtmlBody);
        Assert.Contains("&lt;script&gt;", email.HtmlBody);
        Assert.Contains("A&amp;B &quot;Co&quot;", email.HtmlBody);
        // podział wierszy komentarza zachowany
        Assert.Contains("onerror=alert(1)&gt;<br>druga linia", email.HtmlBody);
        // wersja tekstowa bez kodowania, komentarz jako cytat
        Assert.Contains("> Zapraszamy <img src=x onerror=alert(1)>\n> druga linia", email.TextBody);
        Assert.Contains("zmienił się na: Rozmowa", email.TextBody);
    }

    [Fact]
    public void Subject_IsASingleLineWithoutControlCharactersAndAtMost300Characters()
    {
        var email = Templates.NewApplication("Ola", "Tytuł\r\nBcc: ofiara@example.com\t" + new string('x', 400));

        Assert.DoesNotContain('\r', email.Subject);
        Assert.DoesNotContain('\n', email.Subject);
        Assert.DoesNotContain('\t', email.Subject);
        Assert.StartsWith("Nowa aplikacja: Tytuł Bcc: ofiara@example.com x", email.Subject);
        Assert.Equal(EmailTemplates.MaxSubjectLength, email.Subject.Length);
    }

    [Fact]
    public void NewApplication_DoesNotContainCandidateDataAndLinksToTheEmployerPanel()
    {
        var email = Templates.NewApplication("Ola", "Angular Developer");

        Assert.Contains("„Angular Developer”", email.TextBody);
        Assert.Contains($"{BaseUrl}/employer", email.TextBody);
        Assert.Contains($"{BaseUrl}/account?tab=notifications", email.TextBody);
    }

    [Fact]
    public void CompanyReviewed_Rejected_QuotesTheReason()
    {
        var email = Templates.CompanyReviewed("Ola", "Acme", approved: false, "Brak strony www");

        Assert.Equal("Firma Acme nie została zatwierdzona", email.Subject);
        Assert.Contains("Powód:\n> Brak strony www", email.TextBody);

        var approved = Templates.CompanyReviewed("Ola", "Acme", approved: true, null);
        Assert.Equal("Firma Acme została zatwierdzona", approved.Subject);
        Assert.DoesNotContain("Powód", approved.TextBody);
    }

    [Fact]
    public void Greeting_WithoutAName_IsGeneric()
    {
        var email = Templates.ApplicationSubmitted("  ", "Dev", "Acme");

        Assert.StartsWith("Cześć,\n\n", email.TextBody);
    }

    [Theory]
    [InlineData(30, "30 min")]
    [InlineData(60, "1 godzinę")]
    [InlineData(180, "3 godz.")]
    [InlineData(1440, "1 dzień")]
    [InlineData(4320, "3 dni")]
    public void Describe_FormatsTheValidityInPolish(int minutes, string expected)
    {
        Assert.Equal(expected, EmailTemplates.Describe(TimeSpan.FromMinutes(minutes)));
    }

    [Fact]
    public void StatusLabel_CoversEveryStatus()
    {
        Assert.All(Enum.GetValues<ApplicationStatus>(), status => Assert.NotEqual("Nieznany", EmailTemplates.StatusLabel(status)));
    }
}
