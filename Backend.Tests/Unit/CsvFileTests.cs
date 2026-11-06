using System.Text;
using Backend.Helpers;

namespace Backend.Tests.Unit;

// Eksport CSV: UTF-8 z BOM, separator ";", ochrona przed CSV injection
public class CsvFileTests
{
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+48123456789", "'+48123456789")]
    [InlineData("-2+3", "'-2+3")]
    [InlineData("@SUM(A1)", "'@SUM(A1)")]
    [InlineData("\tcmd", "'\tcmd")]
    [InlineData("=HYPERLINK(\"http://evil\")", "\"'=HYPERLINK(\"\"http://evil\"\")\"")]
    [InlineData("=cmd|' /C calc'!A0;x", "\"'=cmd|' /C calc'!A0;x\"")]
    public void Escape_PrefixesFormulaStartsWithApostrophe(string value, string expected)
    {
        Assert.Equal(expected, CsvFile.Escape(value));
    }

    [Theory]
    [InlineData("plain", "plain")]
    [InlineData("12", "12")]
    [InlineData("a;b", "\"a;b\"")]
    [InlineData("say \"hi\"", "\"say \"\"hi\"\"\"")]
    [InlineData("line1\nline2", "\"line1\nline2\"")]
    [InlineData("", "")]
    public void Escape_QuotesFieldsWithSeparatorsQuotesAndNewlines(string value, string expected)
    {
        Assert.Equal(expected, CsvFile.Escape(value));
    }

    [Fact]
    public void Build_StartsWithUtf8BomAndUsesSemicolonsAndCrLf()
    {
        var bytes = CsvFile.Build(["Data", "Nowi użytkownicy"], [["2026-06-01", "=2"], ["2026-06-02", "3"]]);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal("Data;Nowi użytkownicy\r\n2026-06-01;'=2\r\n2026-06-02;3\r\n", Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3));
    }

    [Fact]
    public void FileName_ContainsTheRange()
    {
        Assert.Equal("statystyki-2026-05-01_2026-05-31.csv",
            CsvFile.FileName(new StatsRange(new DateOnly(2026, 5, 1), new DateOnly(2026, 5, 31))));
    }
}
