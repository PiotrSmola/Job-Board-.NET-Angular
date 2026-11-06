using System.Globalization;

namespace Backend.Helpers;

// Zakres dni statystyk (UTC, obie granice włącznie) z parametrów zapytania from/to
public readonly record struct StatsRange(DateOnly From, DateOnly To)
{
    public const int MaxDays = 366;
    public const int DefaultDays = 30;
    public const string InvalidMessage = "Invalid date range";
    public const string TooLongMessage = "Date range cannot exceed 366 days";

    // rozsądne granice dat - chronią przed przepełnieniem przy wyliczaniu końca zakresu
    private static readonly DateOnly MinDay = new(2000, 1, 1);
    private static readonly DateOnly MaxDay = new(2100, 12, 31);

    public int Days => To.DayNumber - From.DayNumber + 1;

    // początek pierwszego dnia i początek dnia następującego po ostatnim (UTC) - do filtrów [od, do)
    public DateTime StartUtc => From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public DateTime EndExclusiveUtc => To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);

    public IEnumerable<DateOnly> EachDay()
    {
        for (var day = From; day <= To; day = day.AddDays(1))
        {
            yield return day;
        }
    }

    // Brak parametrów = ostatnie 30 dni kończące się dziś; samo from -> do dziś; samo to -> 30 dni wstecz od to.
    // Błąd to komunikat dla odpowiedzi 400.
    public static bool TryCreate(string? from, string? to, DateOnly today, out StatsRange range, out string? error)
    {
        range = default;
        error = InvalidMessage;

        if (!TryParseDay(from, out var fromDay) || !TryParseDay(to, out var toDay))
        {
            return false;
        }

        var end = toDay ?? today;
        var start = fromDay ?? end.AddDays(1 - DefaultDays);
        if (start < MinDay || end > MaxDay || start > end)
        {
            return false;
        }

        range = new StatsRange(start, end);
        if (range.Days > MaxDays)
        {
            error = TooLongMessage;
            return false;
        }

        error = null;
        return true;
    }

    // null/pusty -> brak wartości (true); niepoprawny format -> false
    private static bool TryParseDay(string? value, out DateOnly? day)
    {
        day = null;
        if (string.IsNullOrWhiteSpace(value))
        {
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed))
        {
            day = parsed;
            return true;
        }

        return false;
    }
}
