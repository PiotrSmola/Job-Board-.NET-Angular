using System.Text.RegularExpressions;

namespace Backend.Helpers;

public static partial class CompanyNames
{
    // Nazwa firmy po normalizacji: bez białych znaków na brzegach, wielokrotne odstępy zamienione na jedną spację
    // (ta sama reguła co w migracji AddCompanies) - "  Tech   Corp " i "Tech Corp" to ta sama firma
    public static string Normalize(string name) => Whitespace().Replace(name.Trim(), " ");

    // Puste pole opcjonalne zapisujemy jako null
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();
}
