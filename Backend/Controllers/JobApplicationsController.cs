using System.Security.Claims;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class JobApplicationsController : ControllerBase
{
    private readonly IJobApplicationRepository _applicationRepository;
    private readonly IJobRepository _jobRepository;
    private readonly UserManager<ApplicationUser> _userManager;

    public JobApplicationsController(
        IJobApplicationRepository applicationRepository,
        IJobRepository jobRepository,
        UserManager<ApplicationUser> userManager)
    {
        _applicationRepository = applicationRepository;
        _jobRepository = jobRepository;
        _userManager = userManager;
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

        return Ok(applications.Select(a => a.ToDto()));
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

        return Ok(applications.Select(a => a.ToDto()));
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

        // obronnie: konto mogło zostać usunięte (np. reset danych demo) - inaczej zapis złamałby klucz obcy
        if (!await _userManager.Users.AnyAsync(u => u.Id == userId))
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

        JobApplication createdApplication;
        try
        {
            createdApplication = await _applicationRepository.AddApplicationAsync(application);
        }
        catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
        {
            // równoległe żądanie zdążyło zapisać aplikację - unikalny indeks (JobOfferId, UserId)
            return BadRequest(new { message = "You have already applied to this job" });
        }

        // ponowne pobranie z ofertą i użytkownikiem, żeby zwrócić pełne DTO
        var fullApplication = await _applicationRepository.GetApplicationByIdAsync(createdApplication.Id);
        var applicationDto = (fullApplication ?? createdApplication).ToDto();

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
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetAllApplications([FromQuery] ApplicationParams applicationParams)
    {
        var applications = await _applicationRepository.GetApplicationsAsync(applicationParams);

        Response.AddPaginationHeader(applications);

        return Ok(applications.Select(a => a.ToDto()));
    }
}
