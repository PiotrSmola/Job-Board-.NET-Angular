using System.Text.RegularExpressions;

namespace Backend.Tests.Infrastructure;

// Odczyt źródeł frontendu (stałe, słowniki komunikatów, wzorce) - testy kontraktu porównują je z backendem,
// żeby zmiana po jednej stronie bez drugiej wywróciła test
public static class FrontendSource
{
    public static string Read(string relativePath) =>
        File.ReadAllText(RepositoryFiles.Find(Path.Combine(relativePath.Split('/'))));

    // `export const NAME = 5 * 1024 * 1024;` albo `= 100;`
    public static long Number(string source, string name)
    {
        var match = Regex.Match(source, @"const\s+" + Regex.Escape(name) + @"(?:\s*:\s*[\w<>\[\]]+)?\s*=\s*([\d\s*]+);");
        Assert.True(match.Success, $"Constant {name} not found in the frontend source");
        return match.Groups[1].Value.Split('*', StringSplitOptions.TrimEntries).Select(long.Parse).Aggregate(1L, (a, b) => a * b);
    }

    // Klucze słownika `KNOWN_API_MESSAGES` (komunikaty API tłumaczone na froncie)
    public static HashSet<string> KnownApiMessages()
    {
        var source = Read("frontend/src/app/utils/http-errors.ts");
        var start = source.IndexOf("KNOWN_API_MESSAGES", StringComparison.Ordinal);
        var end = source.IndexOf("};", start, StringComparison.Ordinal);
        var keys = Regex.Matches(source[start..end], @"^\s*(?:'((?:[^'\\]|\\.)*)'|""((?:[^""\\]|\\.)*)""):", RegexOptions.Multiline)
            .Select(m => (m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value).Replace("\\'", "'"))
            .ToHashSet();

        // klucze obliczane: `[TOO_MANY_REQUESTS]: ...` z `const TOO_MANY_REQUESTS = '...';`
        foreach (Match computed in Regex.Matches(source[start..end], @"^\s*\[(\w+)\]:", RegexOptions.Multiline))
        {
            var constant = Regex.Match(source, @"const\s+" + computed.Groups[1].Value + @"\s*=\s*'((?:[^'\\]|\\.)*)'");
            Assert.True(constant.Success, $"Constant {computed.Groups[1].Value} of the message dictionary not found");
            keys.Add(constant.Groups[1].Value);
        }

        return keys;
    }

    // Wzorce `pattern: /^Passwords must .../i` z IDENTITY_PASSWORD_MESSAGES (tłumaczenie opisów błędów hasła z Identity)
    public static IReadOnlyList<Regex> PasswordErrorPatterns()
    {
        var source = Read("frontend/src/app/utils/password.ts");
        return Regex.Matches(source, @"pattern:\s*/(\^[^/]+)/i")
            .Select(m => new Regex(m.Groups[1].Value, RegexOptions.IgnoreCase))
            .ToList();
    }
}

// Zapytanie linku sparsowane tak, jak robi to DefaultUrlSerializer routera Angulara (`router.parseUrl`):
// wartości dzielone po `&`, klucz od wartości po pierwszym `=`, a przed dekodowaniem (decodeURIComponent)
// każdy surowy `+` zamieniany na spację - dlatego "+" w adresie e-mail musi być w linku zapisany jako %2B
public static class AngularRouterQuery
{
    public static Dictionary<string, string> Parse(string pathAndQuery)
    {
        var withoutFragment = pathAndQuery.Split('#')[0];
        var queryStart = withoutFragment.IndexOf('?');
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal);
        if (queryStart < 0)
        {
            return parameters;
        }

        foreach (var pair in withoutFragment[(queryStart + 1)..].Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            var key = separator < 0 ? pair : pair[..separator];
            var value = separator < 0 ? string.Empty : pair[(separator + 1)..];
            parameters[Decode(key)] = Decode(value);
        }

        return parameters;
    }

    private static string Decode(string value) => Uri.UnescapeDataString(value.Replace("+", "%20"));
}
