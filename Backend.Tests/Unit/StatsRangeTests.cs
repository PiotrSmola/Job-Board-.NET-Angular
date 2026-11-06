using Backend.Helpers;

namespace Backend.Tests.Unit;

// Zakres dni statystyk z parametrów from/to: domyślne wartości, granice i komunikaty błędów z kontraktu
public class StatsRangeTests
{
    private static readonly DateOnly Today = new(2026, 6, 15);

    private static (bool Ok, StatsRange Range, string? Error) Create(string? from, string? to)
    {
        var ok = StatsRange.TryCreate(from, to, Today, out var range, out var error);
        return (ok, range, error);
    }

    [Fact]
    public void NoParameters_UsesTheLast30DaysEndingToday()
    {
        var (ok, range, error) = Create(null, null);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(new DateOnly(2026, 5, 17), range.From);
        Assert.Equal(Today, range.To);
        Assert.Equal(30, range.Days);
    }

    [Fact]
    public void OnlyFrom_EndsToday_AndOnlyTo_Starts30DaysBack()
    {
        var onlyFrom = Create("2026-06-01", null);
        var onlyTo = Create(null, "2026-03-31");

        Assert.Equal((new DateOnly(2026, 6, 1), Today), (onlyFrom.Range.From, onlyFrom.Range.To));
        Assert.Equal((new DateOnly(2026, 3, 2), new DateOnly(2026, 3, 31)), (onlyTo.Range.From, onlyTo.Range.To));
    }

    [Fact]
    public void SingleDay_IsAValidRangeWithUtcBounds()
    {
        var (ok, range, _) = Create("2026-02-28", "2026-02-28");

        Assert.True(ok);
        Assert.Equal(1, range.Days);
        Assert.Equal(new DateTime(2026, 2, 28, 0, 0, 0, DateTimeKind.Utc), range.StartUtc);
        Assert.Equal(new DateTime(2026, 3, 1, 0, 0, 0, DateTimeKind.Utc), range.EndExclusiveUtc);
    }

    [Fact]
    public void FromAfterTo_IsInvalid()
    {
        var (ok, _, error) = Create("2026-06-10", "2026-06-09");

        Assert.False(ok);
        Assert.Equal("Invalid date range", error);
    }

    [Fact]
    public void Range_IsLimitedTo366DaysInclusive()
    {
        var max = Create("2025-06-15", "2026-06-15"); // 366 dni (2026-06-15 minus rok + 1)
        var tooLong = Create("2025-06-14", "2026-06-15");

        Assert.True(max.Ok);
        Assert.Equal(366, max.Range.Days);
        Assert.False(tooLong.Ok);
        Assert.Equal("Date range cannot exceed 366 days", tooLong.Error);
    }

    [Theory]
    [InlineData("2026-6-1")]
    [InlineData("01.06.2026")]
    [InlineData("2026-02-30")]
    [InlineData("2026-06-01T00:00:00Z")]
    [InlineData("yesterday")]
    [InlineData("1999-12-31")]
    public void MalformedOrImplausibleDay_IsInvalid(string value)
    {
        Assert.Equal("Invalid date range", Create(value, "2026-06-10").Error);
        Assert.Equal("Invalid date range", Create("2026-06-01", value).Error);
    }

    [Fact]
    public void EachDay_ListsEveryDayOfTheRange()
    {
        var (_, range, _) = Create("2026-02-27", "2026-03-02");

        Assert.Equal(new[] { "2026-02-27", "2026-02-28", "2026-03-01", "2026-03-02" },
            range.EachDay().Select(d => d.ToString("yyyy-MM-dd")));
    }
}
