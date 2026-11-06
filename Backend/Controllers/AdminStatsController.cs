using System.Globalization;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Statystyki całej platformy dla admina
[ApiController]
[Route("api/admin/stats")]
[Authorize(Roles = Roles.Admin)]
public class AdminStatsController : StatsControllerBase
{
    private static readonly string[] CsvHeader = ["Data", "Nowe aplikacje", "Nowe oferty", "Nowi użytkownicy", "Wyświetlenia ofert"];

    private readonly IStatsService _stats;

    public AdminStatsController(IStatsService stats, TimeProvider timeProvider) : base(timeProvider)
    {
        _stats = stats;
    }

    [HttpGet]
    [ProducesResponseType(typeof(AdminStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AdminStatsDto>> GetStats([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (ParseRange(from, to, out var error) is not { } range)
        {
            return error!;
        }

        return Ok(await _stats.GetAdminStatsAsync(range, cancellationToken));
    }

    // Seria dzienna jako CSV
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> ExportStats([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (ParseRange(from, to, out var error) is not { } range)
        {
            return error!;
        }

        var series = await _stats.GetAdminSeriesAsync(range, cancellationToken);
        var rows = series.Select(p => (IReadOnlyList<string>)
        [
            p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            p.NewApplications.ToString(CultureInfo.InvariantCulture),
            p.NewJobs.ToString(CultureInfo.InvariantCulture),
            p.NewUsers.ToString(CultureInfo.InvariantCulture),
            p.JobViews.ToString(CultureInfo.InvariantCulture)
        ]);

        return CsvDownload(range, CsvFile.Build(CsvHeader, rows));
    }
}
