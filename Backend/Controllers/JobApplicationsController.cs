using System.Security.Claims;
using Backend.DTOs;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class JobApplicationsController : ControllerBase
{
    private readonly IJobApplicationRepository _applicationRepository;
    private readonly IJobRepository _jobRepository;

    public JobApplicationsController(
        IJobApplicationRepository applicationRepository,
        IJobRepository jobRepository)
    {
        _applicationRepository = applicationRepository;
        _jobRepository = jobRepository;
    }

    [HttpGet("my-applications")]
    [ProducesResponseType(typeof(IEnumerable<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetMyApplications()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var applications = await _applicationRepository.GetApplicationsByUserIdAsync(userId);

        var applicationsDto = applications.Select(a => new JobApplicationDto
        {
            Id = a.Id,
            JobOfferId = a.JobOfferId,
            UserId = a.UserId,
            Note = a.Note,
            AppliedAt = a.AppliedAt,
            UserEmail = a.User.Email ?? string.Empty,
            JobTitle = a.JobOffer.Title,
            JobOffer = a.JobOffer
        });

        return Ok(applicationsDto);
    }

    [HttpGet("job/{jobId}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetApplicationsByJobId(int jobId)
    {
        if (!await _jobRepository.JobExistsAsync(jobId))
        {
            return NotFound(new { message = "Job offer not found" });
        }

        var applications = await _applicationRepository.GetApplicationsByJobIdAsync(jobId);

        var applicationsDto = applications.Select(a => new JobApplicationDto
        {
            Id = a.Id,
            JobOfferId = a.JobOfferId,
            UserId = a.UserId,
            Note = a.Note,
            AppliedAt = a.AppliedAt,
            UserEmail = a.User.Email ?? string.Empty,
            JobTitle = a.JobOffer.Title,
            JobOffer = a.JobOffer
        });

        return Ok(applicationsDto);
    }

    [HttpPost]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<JobApplicationDto>> ApplyToJob(CreateJobApplicationDto createApplicationDto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        if (!await _jobRepository.JobExistsAsync(createApplicationDto.JobOfferId))
        {
            return NotFound(new { message = "Job offer not found" });
        }

        if (await _applicationRepository.HasUserAppliedAsync(createApplicationDto.JobOfferId, userId))
        {
            return BadRequest(new { message = "You have already applied to this job" });
        }

        var application = new JobApplication
        {
            JobOfferId = createApplicationDto.JobOfferId,
            UserId = userId,
            Note = createApplicationDto.Note,
            AppliedAt = DateTime.UtcNow
        };

        var createdApplication = await _applicationRepository.AddApplicationAsync(application);

        var fullApplication = await _applicationRepository.GetApplicationByIdAsync(createdApplication.Id);

        var applicationDto = new JobApplicationDto
        {
            Id = fullApplication!.Id,
            JobOfferId = fullApplication.JobOfferId,
            UserId = fullApplication.UserId,
            Note = fullApplication.Note,
            AppliedAt = fullApplication.AppliedAt,
            UserEmail = fullApplication.User.Email ?? string.Empty,
            JobTitle = fullApplication.JobOffer.Title
        };

        return CreatedAtAction(nameof(GetMyApplications), new { id = applicationDto.Id }, applicationDto);
    }

    [HttpDelete("{id}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteApplication(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetApplicationByIdAsync(id);

        if (application == null)
        {
            return NotFound(new { message = "Application not found" });
        }

        var isAdmin = User.IsInRole("Admin");
        if (application.UserId != userId && !isAdmin)
        {
            return Forbid();
        }

        var result = await _applicationRepository.DeleteApplicationAsync(id);

        if (!result)
        {
            return NotFound(new { message = "Application not found" });
        }

        return NoContent();
    }

    [HttpGet("check/{jobId}")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<bool>> CheckIfApplied(int jobId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var hasApplied = await _applicationRepository.HasUserAppliedAsync(jobId, userId);
        return Ok(new { hasApplied });
    }

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetAllApplications()
    {
        var applications = await _applicationRepository.GetAllApplicationsAsync();

        var applicationsDto = applications.Select(a => new JobApplicationDto
        {
            Id = a.Id,
            JobOfferId = a.JobOfferId,
            UserId = a.UserId,
            Note = a.Note,
            AppliedAt = a.AppliedAt,
            UserEmail = a.User.Email ?? string.Empty,
            JobTitle = a.JobOffer.Title,
            JobOffer = a.JobOffer
        });

        return Ok(applicationsDto);
    }
}
