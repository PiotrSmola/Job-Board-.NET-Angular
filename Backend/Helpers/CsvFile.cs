using System.Buffers;
using System.Text;

namespace Backend.Helpers;

// Eksport CSV statystyk: UTF-8 z BOM (Excel), separator ";", zakończenia wierszy CRLF
public static class CsvFile
{
    public const string ContentType = "text/csv; charset=utf-8";
    private const char Separator = ';';

    private static readonly SearchValues<char> NeedsQuoting = SearchValues.Create([Separator, '"', '\r', '\n']);

    private static readonly UTF8Encoding Utf8WithBom = new(encoderShouldEmitUTF8Identifier: true);

    public static byte[] Build(IReadOnlyList<string> header, IEnumerable<IReadOnlyList<string>> rows)
    {
        var text = new StringBuilder();
        AppendRow(text, header);
        foreach (var row in rows)
        {
            AppendRow(text, row);
        }

        // GetBytes nie dodaje BOM - dopisujemy go jawnie
        return [.. Utf8WithBom.GetPreamble(), .. Utf8WithBom.GetBytes(text.ToString())];
    }

    // Komórka bezpieczna dla arkuszy kalkulacyjnych: wartości zaczynające się od = + - @ (oraz tabulacji i CR)
    // są poprzedzane apostrofem (CSV injection), a pola z separatorem, cudzysłowem lub końcem wiersza - cytowane
    public static string Escape(string value)
    {
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
        {
            value = "'" + value;
        }

        return value.AsSpan().ContainsAny(NeedsQuoting)
            ? "\"" + value.Replace("\"", "\"\"") + "\""
            : value;
    }

    // Nazwa pliku wg zakresu: statystyki-YYYY-MM-DD_YYYY-MM-DD.csv
    public static string FileName(StatsRange range) => $"statystyki-{range.From:yyyy-MM-dd}_{range.To:yyyy-MM-dd}.csv";

    private static void AppendRow(StringBuilder text, IReadOnlyList<string> cells)
    {
        for (var i = 0; i < cells.Count; i++)
        {
            if (i > 0)
            {
                text.Append(Separator);
            }

            text.Append(Escape(cells[i]));
        }

        text.Append("\r\n");
    }
}
