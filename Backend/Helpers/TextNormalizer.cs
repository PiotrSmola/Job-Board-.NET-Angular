using System.Text.RegularExpressions;

namespace Backend.Helpers;

// Wspólna normalizacja tekstu nazw (firmy, kategorie, umiejętności): bez niewidocznych znaków formatujących
// (\p{Cf}, np. zero-width space, soft hyphen) i kontrolnych, wszystkie białe znaki Unicode (tab, NBSP, ...)
// zwinięte do jednej spacji i obcięte na brzegach
public static partial class TextNormalizer
{
    public static string Normalize(string value) =>
        Whitespace().Replace(Invisible().Replace(value, string.Empty), " ").Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // znaki formatujące i kontrolne poza białymi znakami (tab czy nowa linia stają się spacją)
    [GeneratedRegex(@"[\p{Cf}\p{Cc}-[\s]]")]
    private static partial Regex Invisible();
}
