using System.Text;
using Microsoft.AspNetCore.WebUtilities;

namespace Backend.Services.Email;

// Tokeny Identity w linkach e-mail: Base64Url (bez "+", "/" i "=" - router Angulara zamieniłby "+" w zapytaniu na spację)
public static class EmailTokens
{
    // dłuższy parametr jest odrzucany bez dekodowania (frontend odrzuca tokeny > 2048 znaków)
    public const int MaxEncodedLength = 2048;

    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    public static string Encode(string token) => WebEncoders.Base64UrlEncode(Encoding.UTF8.GetBytes(token));

    public static bool TryDecode(string? encoded, out string token)
    {
        token = string.Empty;
        if (string.IsNullOrEmpty(encoded) || encoded.Length > MaxEncodedLength)
        {
            return false;
        }

        try
        {
            token = StrictUtf8.GetString(WebEncoders.Base64UrlDecode(encoded));
            return token.Length > 0;
        }
        catch (Exception ex) when (ex is FormatException or DecoderFallbackException)
        {
            return false;
        }
    }
}
