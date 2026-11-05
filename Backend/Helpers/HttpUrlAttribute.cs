using System.ComponentModel.DataAnnotations;

namespace Backend.Helpers;

// Bezwzględny adres http(s) z nazwą hosta; puste wartości przepuszcza (od tego jest [Required])
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class HttpUrlAttribute : ValidationAttribute
{
    public HttpUrlAttribute() : base("The {0} field must be an absolute http or https URL.")
    {
    }

    public override bool IsValid(object? value)
    {
        if (value is not string text || string.IsNullOrWhiteSpace(text))
        {
            return true;
        }

        return IsHttpUrl(text.Trim());
    }

    public static bool IsHttpUrl(string text) =>
        Uri.TryCreate(text, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrEmpty(uri.Host) &&
        !text.Any(char.IsWhiteSpace);
}
