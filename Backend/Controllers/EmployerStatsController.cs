using System.Globalization;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Statystyki ofert firmy zalogowanego pracodawcy (firma z bazy, nie z żądania - cudze dane są niedostępne)
[ApiController]
[Route("api/employer/stats")]
[Authorize(Roles = Roles.Employer)]
public class EmployerStatsController : StatsControllerBase
{
    private const string NoCompanyMessage = "Employer account is not linked to a company";
    private static readonly string[] CsvHeader = ["Data", "Nowe aplikacje", "Wyświetlenia ofert"];

    private readonly IStatsService _stats;
    private readonly ICurrentUserAccess _access;

    public EmployerStatsController(IStatsService stats, ICurrentUserAccess access, TimeProvider timeProvider) : base(timeProvider)
    {
        _stats = stats;
        _access = access;
    }

    [HttpGet]
    [ProducesResponseType(typeof(EmployerStatsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<EmployerStatsDto>> GetStats([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (await _access.GetEmployerCompanyIdAsync() is not { } companyId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = NoCompanyMessage });
        }

        if (ParseRange(from, to, out var error) is not { } range)
        {
            return error!;
        }

        return Ok(await _stats.GetEmployerStatsAsync(companyId, range, cancellationToken));
    }

    // Seria dzienna jako CSV
    [HttpGet("export")]
    [ProducesResponseType(typeof(FileContentResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult> ExportStats([FromQuery] string? from, [FromQuery] string? to, CancellationToken cancellationToken)
    {
        if (await _access.GetEmployerCompanyIdAsync() is not { } companyId)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = NoCompanyMessage });
        }

        if (ParseRange(from, to, out var error) is not { } range)
        {
            return error!;
        }

        var series = await _stats.GetEmployerSeriesAsync(companyId, range, cancellationToken);
        var rows = series.Select(p => (IReadOnlyList<string>)
        [
            p.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            p.NewApplications.ToString(CultureInfo.InvariantCulture),
            p.JobViews.ToString(CultureInfo.InvariantCulture)
        ]);

        return CsvDownload(range, CsvFile.Build(CsvHeader, rows));
    }
}
