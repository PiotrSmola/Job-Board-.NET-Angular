using System.Security.Claims;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Backend.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
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
        var (deleted, fileKeys) = await _applicationRepository.DeleteApplicationAsync(id);

        if (!deleted)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        // pliki załączników dopiero po zatwierdzeniu usunięcia w bazie
        await _fileStorage.DeleteQuietlyAsync(fileKeys, _logger);

        return NoContent();
    }

    // Dodanie załączników przez kandydata: multipart/form-data, jedno lub więcej pól "files" i równoległa lista "kinds"
    // (AttachmentKind jako liczba; brak - Other). PDF, DOCX, PNG, JPEG (typ po zawartości) do 5 MB na plik, najwyżej
    // 10 plików i 25 MB łącznie na aplikację. Wszystko albo nic: najpierw walidacja wszystkich plików, potem zapis
    // plików w magazynie, na końcu rekordy w bazie (pod blokadą wiersza aplikacji z kontrolą limitów); gdy zapis
    // w bazie się nie uda albo limit przekroczy równoległe żądanie - zapisane pliki są usuwane.
    [HttpPost("{id:int}/attachments")]
    [EnableRateLimiting(RateLimitingSettings.UploadsPolicy)]
    [RequestSizeLimit(AttachmentFiles.MaxRequestSize)]
    [DisableFormValueModelBinding]
    [ProducesResponseType(typeof(JobApplicationDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<JobApplicationDto>> UploadAttachments(int id)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var cancellationToken = HttpContext.RequestAborted;

        // właściciel i status sprawdzane lekkim zapytaniem PRZED odczytem treści żądania (do 26 MB) - cudza
        // aplikacja wygląda jak nieistniejąca, a jej id nie zmusi serwera do buforowania plików
        var ownership = await _applicationRepository.GetOwnershipAsync(id, cancellationToken);
        if (ownership == null || ownership.UserId != userId)
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        if (!JobApplication.AttachmentUploadStatuses.Contains(ownership.Status))
        {
            return BadRequest(new { message = AttachmentFiles.StatusMessage });
        }

        var form = await AttachmentUploadForm.ReadAsync(HttpContext, cancellationToken);
        if (form.Error != null)
        {
            return BadRequest(new { message = form.Error });
        }

        // walidacja wszystkich plików, zanim cokolwiek trafi do magazynu
        var uploadedAt = DateTime.UtcNow;
        var uploads = new List<(IFormFile File, ApplicationAttachment Attachment)>(form.Items.Count);
        foreach (var item in form.Items)
        {
            var (error, type) = await ValidateAttachmentAsync(item.File, cancellationToken);
            if (type == null)
            {
                return BadRequest(new { message = error });
            }

            uploads.Add((item.File, new ApplicationAttachment
            {
                Kind = item.Kind,
                FileKey = AttachmentFiles.NewStorageKey(id, type),
                FileName = AttachmentFiles.SanitizeFileName(item.File.FileName, type),
                ContentType = type.ContentType,
                Size = item.File.Length,
                UploadedAt = uploadedAt
            }));
        }

        if (uploads.Sum(u => u.Attachment.Size) > AttachmentFiles.MaxTotalSize)
        {
            return BadRequest(new { message = AttachmentFiles.TotalTooLargeMessage });
        }

        var savedKeys = new List<string>(uploads.Count);
        var recordsMayExist = false;
        AttachmentsAddResult result;
        try
        {
            foreach (var (file, attachment) in uploads)
            {
                // klucz dodawany przed zapisem - sprzątany jest też plik, którego zapis się nie udał (magazyn nie
                // zostawia częściowych plików, ale S3 mógł przyjąć obiekt tuż przed zerwaniem połączenia)
                savedKeys.Add(attachment.FileKey);
                await using var content = file.OpenReadStream();
                await _fileStorage.SaveAsync(attachment.FileKey, content, attachment.ContentType, cancellationToken);
            }

            // bez tokenu żądania: po zapisaniu plików operacja jest doprowadzana do końca, żeby przerwanie nie
            // zostawiło niepewnego stanu (rekordy zapisane, a pliki usunięte poniżej)
            recordsMayExist = true;
            result = await _applicationRepository.AddAttachmentsAsync(id, userId,
                uploads.Select(u => u.Attachment).ToList(), CancellationToken.None);
        }
        catch
        {
            if (recordsMayExist)
            {
                await DeleteFilesWithoutRecordsAsync(savedKeys);
            }
            else
            {
                await _fileStorage.DeleteQuietlyAsync(savedKeys, _logger);
            }

            throw;
        }

        if (result != AttachmentsAddResult.Added)
        {
            // stan zmieniło równoległe żądanie (np. decyzja rekrutera, inny upload, usunięcie aplikacji)
            await _fileStorage.DeleteQuietlyAsync(savedKeys, _logger);
            return result switch
            {
                AttachmentsAddResult.ApplicationNotFound => NotFound(new { message = ApplicationNotFoundMessage }),
                AttachmentsAddResult.StatusNotAllowed => BadRequest(new { message = AttachmentFiles.StatusMessage }),
                AttachmentsAddResult.TooMany => BadRequest(new { message = AttachmentFiles.TooManyMessage }),
                _ => BadRequest(new { message = AttachmentFiles.TotalTooLargeMessage })
            };
        }

        var application = await _applicationRepository.GetApplicationByIdAsync(id, includeHistory: true);
        if (application == null)
        {
            // aplikację usunięto zaraz po zapisie - jej pliki usunęło to samo usunięcie
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        // dodaje tylko kandydat - nie widzi autorów zmian
        return CreatedAtAction(nameof(GetApplication), new { id }, application.ToDto(includeHistory: true));
    }

    // Usunięcie załącznika przez kandydata - w każdym statusie aplikacji (prawo do usunięcia danych); plik
    // z magazynu dopiero po zatwierdzeniu usunięcia rekordu
    [HttpDelete("{id:int}/attachments/{attachmentId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeleteAttachment(int id, int attachmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        // przerwanie w trakcie najwyżej osieroci plik (bez rekordu wskazującego na nieistniejący plik)
        var (result, fileKey) = await _applicationRepository.DeleteAttachmentAsync(id, attachmentId, userId, HttpContext.RequestAborted);
        switch (result)
        {
            case AttachmentDeleteResult.ApplicationNotFound:
                return NotFound(new { message = ApplicationNotFoundMessage });
            case AttachmentDeleteResult.AttachmentNotFound:
                return NotFound(new { message = AttachmentFiles.NotFoundMessage });
        }

        await _fileStorage.DeleteQuietlyAsync(fileKey, _logger);

        return NoContent();
    }

    // Pobranie załącznika: kandydat (własna aplikacja), admin, pracodawca firmy oferty; pozostali - 404
    [HttpGet("{id:int}/attachments/{attachmentId:int}")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "image/png", "image/jpeg")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DownloadAttachment(int id, int attachmentId)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            return Unauthorized();
        }

        var cancellationToken = HttpContext.RequestAborted;
        var application = await _applicationRepository.GetApplicationByIdAsync(id, cancellationToken: cancellationToken);
        if (application == null || (application.UserId != userId && !await _access.CanManageApplicationAsync(application)))
        {
            return NotFound(new { message = ApplicationNotFoundMessage });
        }

        var attachment = application.Attachments.FirstOrDefault(a => a.Id == attachmentId);
        if (attachment == null)
        {
            return NotFound(new { message = AttachmentFiles.NotFoundMessage });
        }

        var file = await _fileStorage.OpenReadAsync(attachment.FileKey, cancellationToken);
        if (file == null)
        {
            _logger.LogWarning("Attachment file {FileKey} of application {ApplicationId} is missing in storage",
                attachment.FileKey, application.Id);
            return NotFound(new { message = AttachmentFiles.NotFoundMessage });
        }

        try
        {
            Response.Headers.ContentDisposition = AttachmentFiles.ContentDisposition(attachment.FileName);
            Response.Headers.XContentTypeOptions = "nosniff";
            Response.Headers.CacheControl = "no-store";
            // rozmiar podany przez magazyn (Local - plik, S3 - nagłówek odpowiedzi); strumienia S3 nie da się
            // przewijać, więc FileStreamResult sam by go nie ustawił
            if (file.Length.HasValue)
            {
                Response.ContentLength = file.Length.Value;
            }

            // FileStreamResult zwalnia strumień po wysłaniu odpowiedzi (także przerwanej)
            return File(file.Content, attachment.ContentType);
        }
        catch
        {
            await file.DisposeAsync();
            throw;
        }
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

    // Sprzątanie po błędzie zapisu rekordów załączników: wyjątek mógł wystąpić już po zatwierdzeniu transakcji (np.
    // zerwane połączenie przy potwierdzeniu COMMIT), więc usuwane są tylko pliki, których klucze nie trafiły do bazy.
    // Gdy nie da się tego sprawdzić, pliki zostają - osierocony plik jest mniejszym złem niż rekord bez pliku.
    private async Task DeleteFilesWithoutRecordsAsync(IReadOnlyList<string> savedKeys)
    {
        IReadOnlyList<string> recorded;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            recorded = await _applicationRepository.GetExistingFileKeysAsync(savedKeys, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check which of {Count} saved attachment files have records - the files are kept",
                savedKeys.Count);
            return;
        }

        await _fileStorage.DeleteQuietlyAsync(savedKeys.Except(recorded, StringComparer.Ordinal), _logger);
    }

    // Rozmiar i typ pliku (po zawartości): wykryty typ albo komunikat błędu
    private static async Task<(string? Error, AttachmentFileType? Type)> ValidateAttachmentAsync(IFormFile file,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return (AttachmentFiles.EmptyFileMessage, null);
        }

        if (file.Length > AttachmentFiles.MaxFileSize)
        {
            return (AttachmentFiles.TooLargeMessage, null);
        }

        // strumień pliku z formularza jest przewijalny (bufor ASP.NET Core)
        await using var content = file.OpenReadStream();
        var type = await AttachmentFiles.DetectTypeAsync(content, cancellationToken);
        return type == null ? (AttachmentFiles.UnsupportedTypeMessage, null) : (null, type);
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
