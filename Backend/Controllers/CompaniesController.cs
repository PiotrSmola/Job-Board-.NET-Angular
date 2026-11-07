using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Backend.Settings;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Controllers;

// Publiczne profile firm - lista wyłącznie zatwierdzonych
[Route("api/[controller]")]
[ApiController]
public class CompaniesController : ControllerBase
{
    private readonly ICompanyRepository _companyRepository;
    private readonly ICompanyMediaRepository _mediaRepository;
    private readonly ICurrentUserAccess _access;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<CompaniesController> _logger;

    public CompaniesController(
        ICompanyRepository companyRepository,
        ICompanyMediaRepository mediaRepository,
        ICurrentUserAccess access,
        IFileStorage fileStorage,
        ILogger<CompaniesController> logger)
    {
        _companyRepository = companyRepository;
        _mediaRepository = mediaRepository;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    [HttpGet]
    [EnableRateLimiting(RateLimitingSettings.PublicListsPolicy)]
    [ProducesResponseType(typeof(IEnumerable<CompanyDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CompanyDto>>> GetCompanies([FromQuery] CompanyParams companyParams)
    {
        var companies = await _companyRepository.GetPublicCompaniesAsync(companyParams);

        Response.AddPaginationHeader(companies);

        return Ok(companies.Select(c => c.ToDto(includeReview: false)));
    }

    // Firma zatwierdzona - dla każdego; oczekująca / odrzucona - tylko dla admina i członków firmy (inaczej 404)
    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(CompanyDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyDto>> GetCompany(int id)
    {
        var company = await _companyRepository.GetSummaryAsync(id);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var canManage = await _access.CanManageCompanyAsync(id);
        if (company.Company.Status != CompanyStatus.Approved && !canManage)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        return Ok(company.ToDto(includeReview: canManage));
    }

    // Galeria firmy w kolejności ustalonej przez pracodawcę; widoczność jak profil firmy (zatwierdzona - każdy,
    // pozostałe - admin i członkowie firmy, inni dostają 404)
    [HttpGet("{id:int}/photos")]
    [ProducesResponseType(typeof(IEnumerable<CompanyPhotoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<CompanyPhotoDto>>> GetPhotos(int id)
    {
        if (await VisibleStatusAsync(id) == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var photos = await _mediaRepository.GetPhotosAsync(id, HttpContext.RequestAborted);
        return Ok(photos.Select(p => p.ToDto()));
    }

    // Plik zdjęcia do wyświetlenia (inline); cache publiczny tylko dla firmy zatwierdzonej
    [HttpGet("{id:int}/photos/{photoId:int}/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/png", "image/jpeg")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetPhotoFile(int id, int photoId)
    {
        var status = await VisibleStatusAsync(id);
        if (status == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var cancellationToken = HttpContext.RequestAborted;
        var photo = await _mediaRepository.GetPhotoAsync(id, photoId, cancellationToken);
        if (photo == null)
        {
            return NotFound(new { message = CompanyMedia.PhotoNotFoundMessage });
        }

        var file = await _fileStorage.OpenReadAsync(photo.FileKey, cancellationToken);
        if (file == null)
        {
            _logger.LogWarning("Photo file {FileKey} of company {CompanyId} is missing in storage", photo.FileKey, id);
            return NotFound(new { message = CompanyMedia.PhotoNotFoundMessage });
        }

        var extension = AttachmentFileType.FromContentType(photo.ContentType)?.Extension ?? string.Empty;
        return await this.InlineImageAsync(file, $"company-{id}-photo-{photoId}{extension}", photo.ContentType,
            status == CompanyStatus.Approved);
    }

    // Obraz tła ofert (inline; adres w DTO ma parametr wersji v, ignorowany tutaj); widoczność i cache jak zdjęcia
    [HttpGet("{id:int}/offer-background/file")]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "image/png", "image/jpeg")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetOfferBackgroundFile(int id)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var company = await _mediaRepository.FindCompanyAsync(id, cancellationToken);
        if (company == null || (company.Status != CompanyStatus.Approved && !await _access.CanManageCompanyAsync(id)))
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        if (company.OfferBackgroundKey == null || company.OfferBackgroundContentType == null)
        {
            return NotFound(new { message = CompanyMedia.BackgroundNotFoundMessage });
        }

        var file = await _fileStorage.OpenReadAsync(company.OfferBackgroundKey, cancellationToken);
        if (file == null)
        {
            _logger.LogWarning("Offer background file {FileKey} of company {CompanyId} is missing in storage",
                company.OfferBackgroundKey, id);
            return NotFound(new { message = CompanyMedia.BackgroundNotFoundMessage });
        }

        var extension = AttachmentFileType.FromContentType(company.OfferBackgroundContentType)?.Extension ?? string.Empty;
        return await this.InlineImageAsync(file, $"company-{id}-offer-background{extension}", company.OfferBackgroundContentType,
            company.Status == CompanyStatus.Approved);
    }

    // status firmy, jeśli bieżący użytkownik może ją oglądać (zatwierdzona albo admin / członek), inaczej null
    private async Task<CompanyStatus?> VisibleStatusAsync(int id)
    {
        var status = await _companyRepository.GetStatusAsync(id);
        if (status == null || (status != CompanyStatus.Approved && !await _access.CanManageCompanyAsync(id)))
        {
            return null;
        }

        return status;
    }
}
