using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class JobsController : ControllerBase
{
    private readonly IJobRepository _jobRepository;

    public JobsController(IJobRepository jobRepository)
    {
        _jobRepository = jobRepository;
    }

    // Publiczna lista - wyłącznie oferty aktywne i niewygasłe
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobOfferDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<JobOfferDto>>> GetJobs([FromQuery] JobParams jobParams)
    {
        var jobs = await _jobRepository.GetPublicJobsAsync(jobParams);

        Response.AddPaginationHeader(jobs);

        var now = DateTime.UtcNow;
        return Ok(jobs.Select(j => j.ToDto(now)));
    }

    // Lista dla panelu admina - wszystkie oferty, opcjonalny filtr stanu (draft/active/expired/closed)
    [HttpGet("manage")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<JobOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobOfferDto>>> GetManagedJobs([FromQuery] ManageJobParams jobParams)
    {
        var jobs = await _jobRepository.GetManagedJobsAsync(jobParams);

        Response.AddPaginationHeader(jobs);

        var now = DateTime.UtcNow;
        return Ok(jobs.Select(j => j.ToDto(now)));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobOfferDto>> GetJob(int id)
    {
        var job = await _jobRepository.GetJobByIdAsync(id);

        // szkic widzi tylko admin; oferty zamknięte i wygasłe są zwracane (frontend pokazuje baner)
        if (job == null || (job.Status == JobOfferStatus.Draft && !User.IsInRole("Admin")))
        {
            return NotFound(new { message = "Job offer not found" });
        }

        return Ok(job.ToDto());
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> CreateJob(CreateJobOfferDto createJobDto)
    {
        var job = createJobDto.ToEntity();

        var createdJob = await _jobRepository.AddJobAsync(job);
        var jobDto = createdJob.ToDto();

        return CreatedAtAction(nameof(GetJob), new { id = jobDto.Id }, jobDto);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> UpdateJob(int id, UpdateJobOfferDto updateJobDto)
    {
        // encja pobierana bez śledzenia - UpdateJobAsync dołącza ją do kontekstu jako zmodyfikowaną
        var job = await _jobRepository.GetJobByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Job offer not found" });
        }

        // walidacja daty wymaga zapisanej oferty (status z bazy, gdy żądanie go nie zawiera) - ten sam format błędu co walidacja DTO
        if (updateJobDto.ValidateExpiresAtAgainst(job, DateTime.UtcNow) is { } error)
        {
            ModelState.AddModelError(nameof(UpdateJobOfferDto.ExpiresAt), error.ErrorMessage!);
            return ValidationProblem(ModelState);
        }

        updateJobDto.ApplyTo(job);

        var updatedJob = await _jobRepository.UpdateJobAsync(job);

        return Ok(updatedJob.ToDto());
    }

    // Szybka zmiana stanu oferty: publikacja, zamknięcie, wznowienie, powrót do szkicu
    [HttpPatch("{id}/status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> UpdateJobStatus(int id, UpdateJobOfferStatusDto statusDto)
    {
        var job = await _jobRepository.GetJobByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Job offer not found" });
        }

        var status = statusDto.Status!.Value;
        var now = DateTime.UtcNow;

        // publikacja oferty z datą ważności w przeszłości od razu dałaby ofertę wygasłą
        if (status == JobOfferStatus.Active && job.ExpiresAt.HasValue && job.ExpiresAt.Value <= now)
        {
            return BadRequest(new { message = "Expiration date must be in the future to publish" });
        }

        if (job.Status != status)
        {
            job.Status = status;
            job = await _jobRepository.UpdateJobAsync(job);
        }

        return Ok(job.ToDto(now));
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteJob(int id)
    {
        var result = await _jobRepository.DeleteJobAsync(id);

        if (!result)
        {
            return NotFound(new { message = "Job offer not found" });
        }

        return NoContent();
    }
}
