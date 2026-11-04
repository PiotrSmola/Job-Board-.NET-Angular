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
    private const string AlreadyAppliedMessage = "You have already applied to this job";
    private const string ApplicationNotFoundMessage = "Application not found";
    private const string ConcurrentChangeMessage = "Application status was changed by another request";

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

    // Szczegóły z historią statusów; autor zmian (changedBy) widoczny tylko dla admina
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<JobApplicationDto>> GetApplication(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetApplicationByIdAsync(id, includeHistory: true);
        var isAdmin = User.IsInRole("Admin");

        // cudza aplikacja (poza adminem) - 404 zamiast 403, żeby nie ujawniać jej istnienia
        if (application == null || (application.UserId != userId && !isAdmin))
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        return Ok(application.ToDto(includeHistory: true, includeChangedBy: isAdmin));
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

    // Złożenie aplikacji; na ofertę, z której kandydat wcześniej się wycofał, reaktywuje tę samą aplikację
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

        var job = await _jobRepository.GetJobByIdAsync(createApplicationDto.JobOfferId);
        if (job == null)
        {
            return NotFound(new { message = "Job offer not found" });
        }

        var now = DateTime.UtcNow;
        if (!job.IsPubliclyVisible(now))
        {
            return BadRequest(new { message = "This job offer is not accepting applications" });
        }

        var existing = await _applicationRepository.GetUserApplicationForJobAsync(job.Id, userId);
        int applicationId;

        if (existing != null)
        {
            if (existing.Status != ApplicationStatus.Withdrawn)
            {
                return BadRequest(new { message = AlreadyAppliedMessage });
            }

            // ponowna aplikacja po wycofaniu - ta sama aplikacja wraca do statusu Submitted
            existing.Note = createApplicationDto.Note;
            existing.AppliedAt = now;
            existing.ChangeStatus(ApplicationStatus.Submitted, userId, null, now);

            try
            {
                await _applicationRepository.UpdateApplicationAsync(existing);
            }
            catch (DbUpdateConcurrencyException)
            {
                // równoległe żądanie zdążyło już reaktywować aplikację
                return BadRequest(new { message = AlreadyAppliedMessage });
            }

            applicationId = existing.Id;
        }
        else
        {
            try
            {
                var created = await _applicationRepository.AddApplicationAsync(
                    JobApplication.Submit(job.Id, userId, createApplicationDto.Note, now));
                applicationId = created.Id;
            }
            catch (DbUpdateException ex) when (ex.InnerException is MySqlException { ErrorCode: MySqlErrorCode.DuplicateKeyEntry })
            {
                // równoległe żądanie zdążyło zapisać aplikację - unikalny indeks (JobOfferId, UserId)
                return BadRequest(new { message = AlreadyAppliedMessage });
            }
        }

        // ponowne pobranie z ofertą, użytkownikiem i historią, żeby zwrócić pełne DTO
        var fullApplication = await _applicationRepository.GetApplicationByIdAsync(applicationId, includeHistory: true);
        if (fullApplication == null)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        var applicationDto = fullApplication.ToDto(includeHistory: true, includeChangedBy: User.IsInRole("Admin"));

        return CreatedAtAction(nameof(GetApplication), new { id = applicationDto.Id }, applicationDto);
    }

    // Zmiana statusu przez rekrutera; ten sam status z komentarzem dodaje notatkę do historii
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobApplicationDto>> UpdateStatus(int id, UpdateApplicationStatusDto statusDto)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var status = statusDto.Status!.Value;
        var comment = string.IsNullOrWhiteSpace(statusDto.Comment) ? null : statusDto.Comment.Trim();

        if (status == ApplicationStatus.Withdrawn)
        {
            return BadRequest(new { message = "Only the candidate can withdraw an application" });
        }

        var application = await _applicationRepository.GetApplicationForUpdateAsync(id);
        if (application == null)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (application.Status == ApplicationStatus.Withdrawn)
        {
            return BadRequest(new { message = "Application has been withdrawn by the candidate" });
        }

        if (application.Status == status && comment == null)
        {
            return BadRequest(new { message = "Application already has this status - add a comment to record a note" });
        }

        application.ChangeStatus(status, userId, comment, DateTime.UtcNow);

        try
        {
            await _applicationRepository.UpdateApplicationAsync(application);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = ConcurrentChangeMessage });
        }

        return await ApplicationWithHistoryAsync(id, includeChangedBy: true);
    }

    // Wycofanie aplikacji przez kandydata (tylko z etapów przed decyzją końcową)
    [HttpPost("{id:int}/withdraw")]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<JobApplicationDto>> Withdraw(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetApplicationForUpdateAsync(id);

        // tylko właściciel - cudza aplikacja wygląda jak nieistniejąca
        if (application == null || application.UserId != userId)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (!application.CanBeWithdrawn)
        {
            return BadRequest(new { message = "Application cannot be withdrawn in its current status" });
        }

        application.ChangeStatus(ApplicationStatus.Withdrawn, userId, null, DateTime.UtcNow);

        try
        {
            await _applicationRepository.UpdateApplicationAsync(application);
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { message = ConcurrentChangeMessage });
        }

        return await ApplicationWithHistoryAsync(id, includeChangedBy: User.IsInRole("Admin"));
    }

    // Usuwanie tylko przez admina - kandydat wycofuje aplikację (POST withdraw)
    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteApplication(int id)
    {
        var result = await _applicationRepository.DeleteApplicationAsync(id);

        if (!result)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        return NoContent();
    }

    [HttpGet("check/{jobId}")]
    [ProducesResponseType(typeof(ApplicationCheckDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApplicationCheckDto>> CheckIfApplied(int jobId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetUserApplicationForJobAsync(jobId, userId);

        return Ok(new ApplicationCheckDto
        {
            HasApplied = application != null && application.Status != ApplicationStatus.Withdrawn,
            ApplicationId = application?.Id,
            Status = application?.Status
        });
    }

    [HttpGet("all")]
    [Authorize(Roles = "Admin")]
    [ProducesResponseType(typeof(IEnumerable<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetAllApplications([FromQuery] ApplicationParams applicationParams)
    {
        var applications = await _applicationRepository.GetApplicationsAsync(applicationParams);

        Response.AddPaginationHeader(applications);

        return Ok(applications.Select(a => a.ToDto()));
    }

    private async Task<ActionResult<JobApplicationDto>> ApplicationWithHistoryAsync(int id, bool includeChangedBy)
    {
        var application = await _applicationRepository.GetApplicationByIdAsync(id, includeHistory: true);
        if (application == null)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        return Ok(application.ToDto(includeHistory: true, includeChangedBy));
    }
}
