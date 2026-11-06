using Backend.Helpers;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Wspólne dla statystyk admina i pracodawcy: zakres z parametrów from/to oraz odpowiedź CSV
public abstract class StatsControllerBase : ControllerBase
{
    private readonly TimeProvider _timeProvider;

    protected StatsControllerBase(TimeProvider timeProvider)
    {
        _timeProvider = timeProvider;
    }

    // Zakres z zapytania (domyślnie ostatnie 30 dni); przy błędzie zwraca 400 z komunikatem z kontraktu
    protected StatsRange? ParseRange(string? from, string? to, out ActionResult? error)
    {
        var today = DateOnly.FromDateTime(_timeProvider.GetUtcNow().UtcDateTime);
        if (StatsRange.TryCreate(from, to, today, out var range, out var message))
        {
            error = null;
            return range;
        }

        error = BadRequest(new { message });
        return null;
    }

    // CSV do pobrania: Content-Disposition budowany jak przy załącznikach (filename* w UTF-8)
    protected FileContentResult CsvDownload(StatsRange range, byte[] content)
    {
        Response.Headers.ContentDisposition = AttachmentFiles.ContentDisposition(CsvFile.FileName(range));
        Response.Headers.CacheControl = "no-store";
        return File(content, CsvFile.ContentType);
    }
}
