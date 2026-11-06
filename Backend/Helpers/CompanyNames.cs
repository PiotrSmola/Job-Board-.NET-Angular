using System.Text.RegularExpressions;

namespace Backend.Helpers;

public static partial class CompanyNames
{
    // Nazwa firmy po normalizacji: bez niewidocznych znaków formatujących (\p{Cf}, np. zero-width space, soft hyphen)
    // i kontrolnych, wszystkie białe znaki Unicode (tab, NBSP, ...) zwinięte do jednej spacji i obcięte na brzegach
    // (ta sama reguła odstępów co w migracji AddCompanies) - "  Tech   Corp ", "Tech Corp" i "Tech Corp​"
    // to ta sama firma. Unikalność sprawdza baza w kolacji utf8mb4_0900_ai_ci: bez wielkości liter i bez akcentów.
    public static string Normalize(string name) =>
        Whitespace().Replace(Invisible().Replace(name, string.Empty), " ").Trim();

    // Puste pole opcjonalne zapisujemy jako null
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    // znaki formatujące i kontrolne poza białymi znakami (tab czy nowa linia stają się spacją)
    [GeneratedRegex(@"[\p{Cf}\p{Cc}-[\s]]")]
    private static partial Regex Invisible();
}
