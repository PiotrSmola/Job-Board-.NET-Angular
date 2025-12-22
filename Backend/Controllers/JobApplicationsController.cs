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

    // GET: api/jobapplications/my-applications
    [HttpGet("my-applications")]
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

    // GET: api/jobapplications/job/5
    [HttpGet("job/{jobId}")]
    [Authorize(Roles = "Admin")]
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

    // POST: api/jobapplications
    [HttpPost]
    public async Task<ActionResult<JobApplicationDto>> ApplyToJob(CreateJobApplicationDto createApplicationDto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // Check if job exists
        if (!await _jobRepository.JobExistsAsync(createApplicationDto.JobOfferId))
        {
            return NotFound(new { message = "Job offer not found" });
        }

        // Check if user already applied
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

        // Reload to get navigation properties
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

    // DELETE: api/jobapplications/5
    [HttpDelete("{id}")]
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

        // Only the user who created the application or admin can delete it
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

    // GET: api/jobapplications/check/5
    [HttpGet("check/{jobId}")]
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

    // GET: api/jobapplications/all
    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
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
