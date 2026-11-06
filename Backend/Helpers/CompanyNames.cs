namespace Backend.Helpers;

public static class CompanyNames
{
    // Nazwa firmy po normalizacji (TextNormalizer): "  Tech   Corp ", "Tech Corp" i "Tech Corp​" (ze znakiem
    // niewidocznym) to ta sama firma. Unikalność sprawdza baza w kolacji utf8mb4_0900_ai_ci: bez wielkości liter
    // i bez akcentów.
    public static string Normalize(string name) => TextNormalizer.Normalize(name);

    // Puste pole opcjonalne zapisujemy jako null
    public static string? NormalizeOptional(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
