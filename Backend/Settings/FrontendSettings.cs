namespace Backend.Settings;

// Adres frontendu (sekcja "Frontend") - podstawa linków w e-mailach (potwierdzenie adresu, reset hasła, powiadomienia)
public class FrontendSettings
{
    public const string SectionName = "Frontend";
    public const string DefaultBaseUrl = "http://localhost:4200";

    public string BaseUrl { get; set; } = DefaultBaseUrl;

    // Absolutny adres http(s) bez danych logowania, zapytania i fragmentu; bez końcowego "/".
    // Rzuca InvalidOperationException przy złej konfiguracji (przerywa start aplikacji)
    public void Validate()
    {
        var value = string.IsNullOrWhiteSpace(BaseUrl) ? DefaultBaseUrl : BaseUrl.Trim();

        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment))
        {
            throw new InvalidOperationException(
                $"{SectionName}:BaseUrl must be an absolute http(s) URL without query and fragment (got '{BaseUrl}').");
        }

        BaseUrl = uri.GetLeftPart(UriPartial.Path).TrimEnd('/');
    }
}
