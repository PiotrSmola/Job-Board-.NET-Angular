using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
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

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobOfferDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<JobOfferDto>>> GetJobs([FromQuery] JobParams jobParams)
    {
        var jobs = await _jobRepository.GetJobsAsync(jobParams);

        Response.AddPaginationHeader(jobs);

        return Ok(jobs.Select(j => j.ToDto()));
    }

    [HttpGet("{id}")]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<JobOfferDto>> GetJob(int id)
    {
        var job = await _jobRepository.GetJobByIdAsync(id);

        if (job == null)
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
    public async Task<ActionResult<JobOfferDto>> UpdateJob(int id, CreateJobOfferDto updateJobDto)
    {
        // encja pobierana bez śledzenia - UpdateJobAsync dołącza ją do kontekstu jako zmodyfikowaną
        var job = await _jobRepository.GetJobByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Job offer not found" });
        }

        updateJobDto.ApplyTo(job);

        var updatedJob = await _jobRepository.UpdateJobAsync(job);

        return Ok(updatedJob.ToDto());
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
