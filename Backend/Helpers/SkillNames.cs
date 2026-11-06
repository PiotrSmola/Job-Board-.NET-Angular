namespace Backend.Helpers;

// Nazwy umiejętności: wyświetlana (po normalizacji tekstu) i klucz porównania (małe litery)
public static class SkillNames
{
    public const int MaxLength = 50;
    public const int MaxPerOffer = 15;

    // Przecinek rozdziela elementy list w parametrach zapytań (skills=A,B) - nazwa z przecinkiem zepsułaby filtr
    public const char ListSeparator = ',';

    public static string Normalize(string name) => TextNormalizer.Normalize(name);

    // Klucz porównania zapisany w Skill.NormalizedName: dopasowanie bez wielkości liter
    public static string KeyOf(string normalizedName) => normalizedName.ToLowerInvariant();

    // Poprawna nazwa po normalizacji: 1-50 znaków i bez separatora list
    public static bool IsValid(string normalizedName) =>
        normalizedName.Length is >= 1 and <= MaxLength && !normalizedName.Contains(ListSeparator);
}
