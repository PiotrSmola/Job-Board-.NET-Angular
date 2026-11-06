namespace Backend.Helpers;

// Wspólne parametry stronicowania dla list zwracanych przez API
public abstract class PaginationParams
{
    public const int MaxPageSize = 50;
    public const int DefaultPageSize = 10;

    // maksymalna długość tekstu wyszukiwania i filtrów tekstowych list (SearchTerm, Location)
    public const int MaxSearchLength = 100;

    // dalsze strony i tak byłyby puste (5 mln elementów przy maksymalnym rozmiarze strony)
    public const int MaxPageNumber = 100_000;

    private int _pageNumber = 1;
    private int _pageSize = DefaultPageSize;

    public int PageNumber
    {
        get => _pageNumber;
        set => _pageNumber = Math.Clamp(value, 1, MaxPageNumber);
    }

    public int PageSize
    {
        get => _pageSize;
        set => _pageSize = value < 1 ? DefaultPageSize : Math.Min(value, MaxPageSize);
    }
}
