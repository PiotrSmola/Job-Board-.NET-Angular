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

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
[Authorize]
public class JobApplicationsController : ControllerBase
{
    private const string AlreadyAppliedMessage = "You have already applied to this job";
    private const string ApplicationNotFoundMessage = "Application not found";
    private const string ConcurrentChangeMessage = "Application status was changed by another request";

    private const string JobNotFoundMessage = "Job offer not found";

    private const string CvNotFoundMessage = "CV not found";
    private const string CvStatusMessage = "CV cannot be changed in the current application status";
    private const string CvConcurrentChangeMessage = "CV was changed by another request";
    private const string NoFileMessage = "No file uploaded - send it in the 'file' form field";

    private readonly IJobApplicationRepository _applicationRepository;
    private readonly IJobRepository _jobRepository;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ICurrentUserAccess _access;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<JobApplicationsController> _logger;

    public JobApplicationsController(
        IJobApplicationRepository applicationRepository,
        IJobRepository jobRepository,
        UserManager<ApplicationUser> userManager,
        ICurrentUserAccess access,
        IFileStorage fileStorage,
        ILogger<JobApplicationsController> logger)
    {
        _applicationRepository = applicationRepository;
        _jobRepository = jobRepository;
        _userManager = userManager;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
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

    // Szczegóły z historią statusów: kandydat (własna aplikacja), admin, pracodawca firmy oferty;
    // autor zmian (changedBy) widoczny tylko dla admina i pracodawcy firmy
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
        if (application == null)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        var canManage = await _access.CanManageApplicationAsync(application);

        // cudza aplikacja (poza adminem i pracodawcą firmy) - 404 zamiast 403, żeby nie ujawniać jej istnienia
        if (application.UserId != userId && !canManage)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        return Ok(application.ToDto(includeHistory: true, includeChangedBy: canManage));
    }

    // Aplikacje do oferty - admin albo pracodawca firmy oferty (oferta innej firmy - 404)
    [HttpGet("job/{jobId}")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(typeof(IEnumerable<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetApplicationsByJobId(int jobId)
    {
        var job = await _jobRepository.GetJobByIdAsync(jobId);
        if (job == null || !await _access.CanManageJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
        }

        var applications = await _applicationRepository.GetApplicationsByJobIdAsync(jobId);

        return Ok(applications.Select(a => a.ToDto()));
    }

    // Złożenie aplikacji (tylko kandydaci - nie admin ani pracodawca); na ofertę, z której kandydat wcześniej
    // się wycofał, reaktywuje tę samą aplikację
    [HttpPost]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
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

        if (_access.IsAdmin || _access.IsEmployer)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Only candidates can apply to job offers" });
        }

        var job = await _jobRepository.GetJobByIdAsync(createApplicationDto.JobOfferId);

        // szkic i oferta firmy niezatwierdzonej są dla kandydata niewidoczne (jak w GET /api/jobs/{id}) -
        // 404, żeby nie ujawniać ich istnienia
        if (job == null || !await _access.CanViewJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
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

            try
            {
                // ponowna aplikacja po wycofaniu - ta sama aplikacja wraca do statusu Submitted
                await _applicationRepository.UpdateApplicationAsync(existing, changedAt =>
                {
                    existing.Note = createApplicationDto.Note;
                    existing.AppliedAt = changedAt;
                    existing.ChangeStatus(ApplicationStatus.Submitted, userId, null, changedAt);
                });
            }
            catch (Exception ex) when (ex.IsConcurrentChangeConflict())
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
            catch (Exception ex) when (ex.IsDuplicateKey() || ex.IsConcurrentChangeConflict())
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

        // kandydat nie widzi autorów zmian
        var applicationDto = fullApplication.ToDto(includeHistory: true);

        return CreatedAtAction(nameof(GetApplication), new { id = applicationDto.Id }, applicationDto);
    }

    // Zmiana statusu przez rekrutera (admin albo pracodawca firmy oferty); ten sam status z komentarzem
    // dodaje notatkę do historii
    [HttpPut("{id:int}/status")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
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

        // aplikacja do oferty innej firmy wygląda jak nieistniejąca
        if (application == null || !await _access.CanManageApplicationAsync(application))
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

        try
        {
            await _applicationRepository.UpdateApplicationAsync(application,
                changedAt => application.ChangeStatus(status, userId, comment, changedAt));
        }
        catch (Exception ex) when (ex.IsConcurrentChangeConflict())
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

        try
        {
            await _applicationRepository.UpdateApplicationAsync(application,
                changedAt => application.ChangeStatus(ApplicationStatus.Withdrawn, userId, null, changedAt));
        }
        catch (Exception ex) when (ex.IsConcurrentChangeConflict())
        {
            return Conflict(new { message = ConcurrentChangeMessage });
        }

        // wycofuje tylko kandydat - nie widzi autorów zmian
        return await ApplicationWithHistoryAsync(id, includeChangedBy: false);
    }

    // Usuwanie tylko przez admina - kandydat wycofuje aplikację (POST withdraw)
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.Admin)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteApplication(int id)
    {
        var (deleted, cvFileKey) = await _applicationRepository.DeleteApplicationAsync(id);

        if (!deleted)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        // plik CV dopiero po zatwierdzeniu usunięcia w bazie
        await _fileStorage.DeleteQuietlyAsync(cvFileKey, _logger);

        return NoContent();
    }

    // Dodanie albo zamiana CV przez kandydata (multipart/form-data, pole "file"; PDF lub DOCX do 5 MB, typ
    // rozpoznawany po zawartości). Kolejność: zapis nowego pliku -> warunkowy zapis w bazie -> usunięcie starego
    // pliku; gdy zapis w bazie się nie uda, nowy plik jest usuwany.
    [HttpPut("{id:int}/cv")]
    [CvUploadForm]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<JobApplicationDto>> UploadCv(int id, IFormFile? file)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetApplicationByIdAsync(id, cancellationToken: HttpContext.RequestAborted);

        // tylko właściciel - cudza aplikacja wygląda jak nieistniejąca
        if (application == null || application.UserId != userId)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (!application.CanChangeCv)
        {
            return BadRequest(new { message = CvStatusMessage });
        }

        if (file == null)
        {
            return BadRequest(new { message = NoFileMessage });
        }

        if (file.Length == 0)
        {
            return BadRequest(new { message = CvFiles.EmptyFileMessage });
        }

        if (file.Length > CvFiles.MaxFileSize)
        {
            return BadRequest(new { message = CvFiles.TooLargeMessage });
        }

        // strumień pliku z formularza jest przewijalny (bufor ASP.NET Core) - po sprawdzeniu typu wraca na początek
        await using var content = file.OpenReadStream();
        var type = await CvFiles.DetectTypeAsync(content, HttpContext.RequestAborted);
        if (type == null)
        {
            return BadRequest(new { message = CvFiles.UnsupportedTypeMessage });
        }

        var cv = new CvFile(
            CvFiles.NewStorageKey(id, type),
            CvFiles.SanitizeFileName(file.FileName, type),
            type.ContentType,
            file.Length,
            DateTime.UtcNow);

        await _fileStorage.SaveAsync(cv.Key, content, cv.ContentType, HttpContext.RequestAborted);

        bool updated;
        try
        {
            // bez tokenu żądania: przerwanie w trakcie zapisu mogłoby zostawić niepewny stan (zapis wykonany,
            // a nowy plik usunięty poniżej) - po zapisaniu pliku operacja jest doprowadzana do końca
            updated = await _applicationRepository.UpdateCvAsync(id, userId, application.CvFileKey, cv, CancellationToken.None);
        }
        catch
        {
            await _fileStorage.DeleteQuietlyAsync(cv.Key, _logger);
            throw;
        }

        if (!updated)
        {
            await _fileStorage.DeleteQuietlyAsync(cv.Key, _logger);
            return await CvChangeRejectedAsync(id, userId, requireCv: false);
        }

        await _fileStorage.DeleteQuietlyAsync(application.CvFileKey, _logger);

        // zmienia tylko kandydat - nie widzi autorów zmian
        return await ApplicationWithHistoryAsync(id, includeChangedBy: false);
    }

    // Usunięcie CV przez kandydata (te same statusy co przy wgrywaniu)
    [HttpDelete("{id:int}/cv")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteCv(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetApplicationByIdAsync(id, cancellationToken: HttpContext.RequestAborted);
        if (application == null || application.UserId != userId)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (!application.HasCv)
        {
            return NotFound(new { message = CvNotFoundMessage });
        }

        if (!application.CanChangeCv)
        {
            return BadRequest(new { message = CvStatusMessage });
        }

        // przerwanie w trakcie zapisu najwyżej osieroci plik (bez odwołania w bazie do nieistniejącego pliku)
        if (!await _applicationRepository.UpdateCvAsync(id, userId, application.CvFileKey, null, HttpContext.RequestAborted))
        {
            return await CvChangeRejectedAsync(id, userId, requireCv: true);
        }

        await _fileStorage.DeleteQuietlyAsync(application.CvFileKey, _logger);

        return NoContent();
    }

    // Pobranie CV: kandydat (własna aplikacja), admin, pracodawca firmy oferty; pozostali - 404
    [HttpGet("{id:int}/cv")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadCv(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var application = await _applicationRepository.GetApplicationByIdAsync(id, cancellationToken: HttpContext.RequestAborted);
        if (application == null || (application.UserId != userId && !await _access.CanManageApplicationAsync(application)))
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (!application.HasCv)
        {
            return NotFound(new { message = CvNotFoundMessage });
        }

        var content = await _fileStorage.OpenReadAsync(application.CvFileKey!, HttpContext.RequestAborted);
        if (content == null)
        {
            _logger.LogWarning("CV file {FileKey} of application {ApplicationId} is missing in storage",
                application.CvFileKey, application.Id);
            return NotFound(new { message = CvNotFoundMessage });
        }

        Response.Headers.ContentDisposition = CvFiles.ContentDisposition(application.CvFileName ?? "cv");
        Response.Headers.XContentTypeOptions = "nosniff";
        Response.Headers.CacheControl = "no-store";

        return File(content, application.CvContentType ?? "application/octet-stream");
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

    // Panel zarządzania: admin - wszystkie aplikacje, pracodawca - tylko do ofert swojej firmy
    [HttpGet("all")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(typeof(IEnumerable<JobApplicationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobApplicationDto>>> GetAllApplications([FromQuery] ApplicationParams applicationParams)
    {
        var scope = await _access.GetManagementScopeAsync();
        if (scope == null)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Employer account is not linked to a company" });
        }

        var applications = await _applicationRepository.GetApplicationsAsync(applicationParams, scope);

        Response.AddPaginationHeader(applications);

        return Ok(applications.Select(a => a.ToDto()));
    }

    // Warunkowy zapis CV nie trafił w żaden wiersz - powód ustalany na aktualnym stanie aplikacji
    private async Task<ObjectResult> CvChangeRejectedAsync(int id, string userId, bool requireCv)
    {
        var current = await _applicationRepository.GetApplicationByIdAsync(id, cancellationToken: HttpContext.RequestAborted);
        if (current == null || current.UserId != userId)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (requireCv && !current.HasCv)
        {
            return NotFound(new { message = CvNotFoundMessage });
        }

        if (!current.CanChangeCv)
        {
            return BadRequest(new { message = CvStatusMessage });
        }

        // równoległe żądanie zmieniło CV w międzyczasie
        return Conflict(new { message = CvConcurrentChangeMessage });
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
