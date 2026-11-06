using System.Security.Claims;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

// Zapisane oferty - tylko kandydat (pracodawca i admin dostają 403)
[Route("api/saved-jobs")]
[ApiController]
[Authorize(Roles = Roles.User)]
public class SavedJobsController : ControllerBase
{
    private const string JobNotFoundMessage = "Job offer not found";

    private readonly ISavedJobRepository _savedJobRepository;
    private readonly IJobRepository _jobRepository;
    private readonly ICurrentUserAccess _access;

    public SavedJobsController(ISavedJobRepository savedJobRepository, IJobRepository jobRepository, ICurrentUserAccess access)
    {
        _savedJobRepository = savedJobRepository;
        _jobRepository = jobRepository;
        _access = access;
    }

    // Od najnowszych zapisów; także oferty, które w międzyczasie wygasły albo zamknięto (JobOfferDto ma status
    // i isExpired), bez ofert niewidocznych dla kandydata (szkic, firma niezatwierdzona)
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<SavedJobDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<SavedJobDto>>> GetSavedJobs([FromQuery] SavedJobParams savedJobParams)
    {
        var savedJobs = await _savedJobRepository.GetSavedJobsAsync(UserId, savedJobParams);

        Response.AddPaginationHeader(savedJobs);

        var now = DateTime.UtcNow;
        return Ok(savedJobs.Select(s => new SavedJobDto { SavedAt = s.SavedAt, Job = s.JobOffer.ToDto(now) }));
    }

    // Id zapisanych ofert (oznaczanie przycisków na liście bez zapytania na każdą ofertę)
    [HttpGet("ids")]
    [ProducesResponseType(typeof(IEnumerable<int>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<int>>> GetSavedJobIds()
    {
        return Ok(await _savedJobRepository.GetSavedJobIdsAsync(UserId));
    }

    // Idempotentne; oferta niewidoczna dla kandydata (szkic, firma niezatwierdzona) wygląda jak nieistniejąca
    [HttpPut("{jobId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> SaveJob(int jobId)
    {
        var job = await _jobRepository.GetJobByIdAsync(jobId);
        if (job == null || !await _access.CanViewJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
        }

        if (!await _savedJobRepository.SaveAsync(UserId, jobId, DateTime.UtcNow))
        {
            // oferta usunięta w międzyczasie
            return NotFound(new { message = JobNotFoundMessage });
        }

        return NoContent();
    }

    // Idempotentne - usunięcie niezapisanej oferty też daje 204
    [HttpDelete("{jobId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> RemoveSavedJob(int jobId)
    {
        await _savedJobRepository.RemoveAsync(UserId, jobId);
        return NoContent();
    }

    // [Authorize] gwarantuje uwierzytelnienie; JwtUserValidator - że konto nadal istnieje
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;
}
