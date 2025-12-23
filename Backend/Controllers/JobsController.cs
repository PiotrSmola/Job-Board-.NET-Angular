using System.Security.Claims;
using System.Text.Json;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
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

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobOfferDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<JobOfferDto>>> GetJobs([FromQuery] JobParams jobParams)
    {
        var jobs = await _jobRepository.GetJobsAsync(jobParams);

        // Add pagination header
        var paginationHeader = new PaginationHeader(
            jobs.CurrentPage,
            jobs.PageSize,
            jobs.TotalCount,
            jobs.TotalPages
        );

        Response.Headers.Append("Pagination", JsonSerializer.Serialize(paginationHeader));

        var jobsDto = jobs.Select(j => new JobOfferDto
        {
            Id = j.Id,
            Title = j.Title,
            Company = j.Company,
            Description = j.Description,
            SalaryMin = j.SalaryMin,
            SalaryMax = j.SalaryMax,
            Location = j.Location,
            WorkType = j.WorkType,
            CreatedAt = j.CreatedAt
        });

        return Ok(jobsDto);
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

        var jobDto = new JobOfferDto
        {
            Id = job.Id,
            Title = job.Title,
            Company = job.Company,
            Description = job.Description,
            SalaryMin = job.SalaryMin,
            SalaryMax = job.SalaryMax,
            Location = job.Location,
            WorkType = job.WorkType,
            CreatedAt = job.CreatedAt
        };

        return Ok(jobDto);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> CreateJob(CreateJobOfferDto createJobDto)
    {
        var job = new JobOffer
        {
            Title = createJobDto.Title,
            Company = createJobDto.Company,
            Description = createJobDto.Description,
            SalaryMin = createJobDto.SalaryMin,
            SalaryMax = createJobDto.SalaryMax,
            Location = createJobDto.Location,
            WorkType = createJobDto.WorkType,
            CreatedAt = DateTime.UtcNow
        };

        var createdJob = await _jobRepository.AddJobAsync(job);

        var jobDto = new JobOfferDto
        {
            Id = createdJob.Id,
            Title = createdJob.Title,
            Company = createdJob.Company,
            Description = createdJob.Description,
            SalaryMin = createdJob.SalaryMin,
            SalaryMax = createdJob.SalaryMax,
            Location = createdJob.Location,
            WorkType = createdJob.WorkType,
            CreatedAt = createdJob.CreatedAt
        };

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
        if (!await _jobRepository.JobExistsAsync(id))
        {
            return NotFound(new { message = "Job offer not found" });
        }

        var job = await _jobRepository.GetJobByIdAsync(id);
        if (job == null)
        {
            return NotFound(new { message = "Job offer not found" });
        }

        job.Title = updateJobDto.Title;
        job.Company = updateJobDto.Company;
        job.Description = updateJobDto.Description;
        job.SalaryMin = updateJobDto.SalaryMin;
        job.SalaryMax = updateJobDto.SalaryMax;
        job.Location = updateJobDto.Location;
        job.WorkType = updateJobDto.WorkType;

        var updatedJob = await _jobRepository.UpdateJobAsync(job);

        var jobDto = new JobOfferDto
        {
            Id = updatedJob.Id,
            Title = updatedJob.Title,
            Company = updatedJob.Company,
            Description = updatedJob.Description,
            SalaryMin = updatedJob.SalaryMin,
            SalaryMax = updatedJob.SalaryMax,
            Location = updatedJob.Location,
            WorkType = updatedJob.WorkType,
            CreatedAt = updatedJob.CreatedAt
        };

        return Ok(jobDto);
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
