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
    private const string JobNotFoundMessage = "Job offer not found";
    private const string CompanyNotApprovedMessage = "Company must be approved before publishing offers";
    private const string NoCompanyMessage = "Employer account is not linked to a company";
    private const string JobHasApplicationsMessage = "Job offer has applications - close it instead";

    private readonly IJobRepository _jobRepository;
    private readonly ICompanyRepository _companyRepository;
    private readonly ICategoryRepository _categoryRepository;
    private readonly ICurrentUserAccess _access;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IJobRepository jobRepository,
        ICompanyRepository companyRepository,
        ICategoryRepository categoryRepository,
        ICurrentUserAccess access,
        IFileStorage fileStorage,
        ILogger<JobsController> logger)
    {
        _jobRepository = jobRepository;
        _companyRepository = companyRepository;
        _categoryRepository = categoryRepository;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    // Publiczna lista - wyłącznie oferty aktywne i niewygasłe firm zatwierdzonych (opcjonalne filtry: companyId,
    // kategoria, poziom, typy umów, tryb pracy, umiejętności, waluta i widełki - łączone AND)
    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<JobOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<IEnumerable<JobOfferDto>>> GetJobs([FromQuery] JobParams jobParams)
    {
        var jobs = await _jobRepository.GetPublicJobsAsync(jobParams);

        Response.AddPaginationHeader(jobs);

        var now = DateTime.UtcNow;
        return Ok(jobs.Select(j => j.ToDto(now)));
    }

    // Liczniki faset publicznej listy (te same filtry co GET /api/jobs, bez stronicowania); licznik fasety
    // pomija jej własny filtr, żeby można było rozszerzać wybór w grupie
    [HttpGet("facets")]
    [ProducesResponseType(typeof(JobFacetsDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<JobFacetsDto>> GetFacets([FromQuery] JobParams jobParams, CancellationToken cancellationToken)
    {
        var facets = await _jobRepository.GetPublicFacetsAsync(jobParams, cancellationToken);
        return Ok(facets.ToDto());
    }

    // Panel zarządzania: admin - wszystkie oferty (opcjonalny filtr companyId), pracodawca - wyłącznie oferty
    // swojej firmy; opcjonalny filtr stanu (draft/active/expired/closed)
    [HttpGet("manage")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(typeof(IEnumerable<JobOfferDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<JobOfferDto>>> GetManagedJobs([FromQuery] ManageJobParams jobParams)
    {
        var scope = await _access.GetManagementScopeAsync();
        if (scope == null)
        {
            return Forbidden(NoCompanyMessage);
        }

        var jobs = await _jobRepository.GetManagedJobsAsync(jobParams, scope);

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

        // szkice i oferty firm niezatwierdzonych widzi tylko admin i pracodawca firmy;
        // oferty zamknięte i wygasłe są zwracane (frontend pokazuje baner)
        if (job == null || !await _access.CanViewJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
        }

        return Ok(job.ToDto());
    }

    [HttpPost]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> CreateJob(CreateJobOfferDto createJobDto)
    {
        var (company, error) = await ResolveCompanyAsync(createJobDto.CompanyId, offerCompanyId: null);
        if (error != null)
        {
            return error;
        }

        if (createJobDto.Status == JobOfferStatus.Active && !CanPublishIn(company!.Value.Status))
        {
            return Forbidden(CompanyNotApprovedMessage);
        }

        if (await CategoryMissingAsync(createJobDto.CategoryId))
        {
            return BadRequest(new { message = CategoryMessages.NotFound });
        }

        var createdJob = await _jobRepository.AddJobAsync(createJobDto.ToEntity(company!.Value.Id), createJobDto.GetSkillNames());
        var jobDto = createdJob.ToDto();

        return CreatedAtAction(nameof(GetJob), new { id = jobDto.Id }, jobDto);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> UpdateJob(int id, UpdateJobOfferDto updateJobDto)
    {
        // encja pobierana bez śledzenia - UpdateJobAsync dołącza ją do kontekstu jako zmodyfikowaną
        var job = await _jobRepository.GetJobByIdAsync(id);

        // oferta innej firmy wygląda jak nieistniejąca
        if (job == null || !await _access.CanManageJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
        }

        var (company, error) = await ResolveCompanyAsync(updateJobDto.CompanyId, job.CompanyId);
        if (error != null)
        {
            return error;
        }

        // zapis, po którym oferta byłaby aktywna (publikacja albo edycja aktywnej oferty)
        if ((updateJobDto.Status ?? job.Status) == JobOfferStatus.Active && !CanPublishIn(company!.Value.Status))
        {
            return Forbidden(CompanyNotApprovedMessage);
        }

        // walidacja daty wymaga zapisanej oferty (status z bazy, gdy żądanie go nie zawiera) - ten sam format błędu co walidacja DTO
        if (updateJobDto.ValidateExpiresAtAgainst(job, DateTime.UtcNow) is { } validationError)
        {
            ModelState.AddModelError(nameof(UpdateJobOfferDto.ExpiresAt), validationError.ErrorMessage!);
            return ValidationProblem(ModelState);
        }

        if (await CategoryMissingAsync(updateJobDto.CategoryId))
        {
            return BadRequest(new { message = CategoryMessages.NotFound });
        }

        updateJobDto.ApplyTo(job, company!.Value.Id);

        var updatedJob = await _jobRepository.UpdateJobAsync(job, updateJobDto.GetSkillNames());

        return Ok(updatedJob.ToDto());
    }

    // Szybka zmiana stanu oferty: publikacja, zamknięcie, wznowienie, powrót do szkicu
    [HttpPatch("{id}/status")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(typeof(JobOfferDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<JobOfferDto>> UpdateJobStatus(int id, UpdateJobOfferStatusDto statusDto)
    {
        var job = await _jobRepository.GetJobByIdAsync(id);
        if (job == null || !await _access.CanManageJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
        }

        var status = statusDto.Status!.Value;
        var now = DateTime.UtcNow;

        if (status == JobOfferStatus.Active && !CanPublishIn(job.Company.Status))
        {
            return Forbidden(CompanyNotApprovedMessage);
        }

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

    // Admin usuwa ofertę razem z aplikacjami (kaskadowo, z plikami załączników); pracodawca tylko ofertę bez aplikacji -
    // ofertę z aplikacjami kandydatów może zamknąć, ale nie usunąć ich danych
    [HttpDelete("{id}")]
    [Authorize(Roles = Roles.AdminOrEmployer)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteJob(int id)
    {
        var job = await _jobRepository.GetJobByIdAsync(id);
        if (job == null || !await _access.CanManageJobAsync(job))
        {
            return NotFound(new { message = JobNotFoundMessage });
        }

        if (_access.IsAdmin)
        {
            var (deleted, fileKeys) = await _jobRepository.DeleteJobAsync(id);
            if (!deleted)
            {
                return NotFound(new { message = JobNotFoundMessage });
            }

            // pliki załączników usuniętych aplikacji dopiero po zatwierdzeniu usunięcia w bazie
            await _fileStorage.DeleteQuietlyAsync(fileKeys, _logger);
            return NoContent();
        }

        if (await _jobRepository.DeleteJobWithoutApplicationsAsync(id))
        {
            return NoContent();
        }

        // nic nie usunięto: oferta ma aplikacje albo w międzyczasie zniknęła
        return await _jobRepository.JobExistsAsync(id)
            ? BadRequest(new { message = JobHasApplicationsMessage })
            : NotFound(new { message = JobNotFoundMessage });
    }

    // Firma oferty: admin wskazuje ją w żądaniu (companyId wymagane), pracodawca - zawsze własna firma
    // (companyId z żądania ignorowane; edytowana oferta pracodawcy należy już do jego firmy)
    private async Task<((int Id, CompanyStatus Status)? Company, ActionResult? Error)> ResolveCompanyAsync(
        int? requestedCompanyId, int? offerCompanyId)
    {
        int companyId;
        if (_access.IsAdmin)
        {
            if (!requestedCompanyId.HasValue)
            {
                ModelState.AddModelError(nameof(JobOfferFormDto.CompanyId), "The CompanyId field is required.");
                return (null, ValidationProblem(ModelState));
            }

            companyId = requestedCompanyId.Value;
        }
        else
        {
            var employerCompanyId = offerCompanyId ?? await _access.GetEmployerCompanyIdAsync();
            if (!employerCompanyId.HasValue)
            {
                return (null, Forbidden(NoCompanyMessage));
            }

            companyId = employerCompanyId.Value;
        }

        var status = await _companyRepository.GetStatusAsync(companyId);
        if (!status.HasValue)
        {
            return (null, BadRequest(new { message = "Company not found" }));
        }

        return ((companyId, status.Value), null);
    }

    // Wskazana kategoria nie istnieje (null = oferta bez kategorii)
    private async Task<bool> CategoryMissingAsync(int? categoryId) =>
        categoryId.HasValue && !await _categoryRepository.ExistsAsync(categoryId.Value);

    // Admin publikuje oferty każdej firmy; pracodawca - dopiero po zatwierdzeniu jego firmy
    private bool CanPublishIn(CompanyStatus companyStatus) =>
        _access.IsAdmin || companyStatus == CompanyStatus.Approved;

    private ObjectResult Forbidden(string message) =>
        StatusCode(StatusCodes.Status403Forbidden, new { message });
}
