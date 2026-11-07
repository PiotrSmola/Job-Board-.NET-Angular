using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Mappings;
using Backend.Models;
using Backend.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Controllers;

// Galeria zdjęć i tło ofert własnej firmy zalogowanego pracodawcy. Firma wynika z konta (ICurrentUserAccess) -
// identyfikatory zdjęć innych firm wyglądają jak nieistniejące (404). Pliki obrazów (PNG, JPEG po zawartości, do 5 MB,
// wymiary z nagłówka) trafiają do magazynu przed zapisem rekordów; usunięte rekordy - pliki po zatwierdzeniu zmian.
[Route("api/employer/company")]
[ApiController]
[Authorize(Roles = Roles.Employer)]
public class EmployerCompanyMediaController : ControllerBase
{
    private readonly ICompanyMediaRepository _mediaRepository;
    private readonly ICurrentUserAccess _access;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<EmployerCompanyMediaController> _logger;

    public EmployerCompanyMediaController(
        ICompanyMediaRepository mediaRepository,
        ICurrentUserAccess access,
        IFileStorage fileStorage,
        ILogger<EmployerCompanyMediaController> logger)
    {
        _mediaRepository = mediaRepository;
        _access = access;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    // Dodanie zdjęć: multipart/form-data, jedno lub więcej pól "files". Wszystko albo nic: walidacja wszystkich plików,
    // zapis plików w magazynie, na końcu rekordy pod blokadą wiersza firmy z kontrolą limitu 20 zdjęć; gdy zapis w bazie
    // się nie uda albo limit przekroczy równoległe żądanie - zapisane pliki są usuwane. Odpowiedź: 201 z całą galerią.
    [HttpPost("photos")]
    [EnableRateLimiting(RateLimitingSettings.UploadsPolicy)]
    [RequestSizeLimit(AttachmentFiles.MaxRequestSize)]
    [DisableFormValueModelBinding]
    [ProducesResponseType(typeof(IEnumerable<CompanyPhotoDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IEnumerable<CompanyPhotoDto>>> UploadPhotos()
    {
        var cancellationToken = HttpContext.RequestAborted;

        // firma i pełna galeria sprawdzane lekkimi zapytaniami PRZED odczytem treści żądania (do 26 MB)
        var companyId = await _access.GetEmployerCompanyIdAsync();
        if (companyId == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        if (await _mediaRepository.CountPhotosAsync(companyId.Value, cancellationToken) >= CompanyMedia.MaxPhotos)
        {
            return BadRequest(new { message = CompanyMedia.TooManyPhotosMessage });
        }

        var form = await CompanyMedia.ReadPhotosAsync(HttpContext, cancellationToken);
        if (form.Error != null)
        {
            return BadRequest(new { message = form.Error });
        }

        // walidacja wszystkich plików, zanim cokolwiek trafi do magazynu
        var uploadedAt = DateTime.UtcNow;
        var uploads = new List<(IFormFile File, CompanyPhoto Photo)>(form.Files.Count);
        foreach (var file in form.Files)
        {
            var validation = await ImageFiles.ValidateAsync(file, cancellationToken);
            if (validation.Image == null)
            {
                return BadRequest(new { message = validation.Error });
            }

            var image = validation.Image;
            uploads.Add((file, new CompanyPhoto
            {
                FileKey = CompanyMedia.NewPhotoStorageKey(companyId.Value, image.Type),
                ContentType = image.Type.ContentType,
                Size = file.Length,
                Width = image.Width,
                Height = image.Height,
                UploadedAt = uploadedAt
            }));
        }

        var savedKeys = new List<string>(uploads.Count);
        var recordsMayExist = false;
        PhotosAddResult result;
        try
        {
            foreach (var (file, photo) in uploads)
            {
                // klucz dodawany przed zapisem - sprzątany jest też plik, którego zapis się nie udał
                savedKeys.Add(photo.FileKey);
                await using var content = file.OpenReadStream();
                await _fileStorage.SaveAsync(photo.FileKey, content, photo.ContentType, cancellationToken);
            }

            // bez tokenu żądania: po zapisaniu plików operacja jest doprowadzana do końca (jak przy załącznikach)
            recordsMayExist = true;
            result = await _mediaRepository.AddPhotosAsync(companyId.Value, uploads.Select(u => u.Photo).ToList(),
                CancellationToken.None);
        }
        catch
        {
            if (recordsMayExist)
            {
                await DeletePhotoFilesWithoutRecordsAsync(savedKeys);
            }
            else
            {
                await _fileStorage.DeleteQuietlyAsync(savedKeys, _logger);
            }

            throw;
        }

        if (result != PhotosAddResult.Added)
        {
            // limit przekroczyło równoległe żądanie albo firmę usunięto
            await _fileStorage.DeleteQuietlyAsync(savedKeys, _logger);
            return result == PhotosAddResult.CompanyNotFound
                ? NotFound(new { message = CompanyMessages.NotFound })
                : BadRequest(new { message = CompanyMedia.TooManyPhotosMessage });
        }

        var photos = await _mediaRepository.GetPhotosAsync(companyId.Value, CancellationToken.None);
        return CreatedAtAction(nameof(CompaniesController.GetPhotos), "Companies", new { id = companyId.Value },
            photos.Select(p => p.ToDto()).ToList());
    }

    // Podpis zdjęcia (do 150 znaków; pusty - bez podpisu); odpowiedź: zmienione zdjęcie
    [HttpPatch("photos/{photoId:int}")]
    [ProducesResponseType(typeof(CompanyPhotoDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CompanyPhotoDto>> UpdatePhoto(int photoId, UpdateCompanyPhotoDto photoDto)
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        if (companyId == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var photo = await _mediaRepository.UpdateCaptionAsync(companyId.Value, photoId,
            CompanyMedia.NormalizeCaption(photoDto.Caption), HttpContext.RequestAborted);
        if (photo == null)
        {
            return NotFound(new { message = CompanyMedia.PhotoNotFoundMessage });
        }

        return Ok(photo.ToDto());
    }

    // Nowa kolejność galerii: wszystkie zdjęcia firmy, każde dokładnie raz; odpowiedź: cała galeria
    [HttpPut("photos/order")]
    [ProducesResponseType(typeof(IEnumerable<CompanyPhotoDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<CompanyPhotoDto>>> ReorderPhotos(CompanyPhotoOrderDto orderDto)
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        if (companyId == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        if (orderDto.PhotoIds == null || orderDto.PhotoIds.Count > CompanyMedia.MaxPhotos)
        {
            return BadRequest(new { message = CompanyMedia.InvalidOrderMessage });
        }

        var cancellationToken = HttpContext.RequestAborted;
        var result = await _mediaRepository.ReorderPhotosAsync(companyId.Value, orderDto.PhotoIds, cancellationToken);
        switch (result)
        {
            case PhotoOrderResult.CompanyNotFound:
                return NotFound(new { message = CompanyMessages.NotFound });
            case PhotoOrderResult.Invalid:
                return BadRequest(new { message = CompanyMedia.InvalidOrderMessage });
        }

        var photos = await _mediaRepository.GetPhotosAsync(companyId.Value, cancellationToken);
        return Ok(photos.Select(p => p.ToDto()));
    }

    // Usunięcie zdjęcia; plik z magazynu dopiero po usunięciu rekordu
    [HttpDelete("photos/{photoId:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeletePhoto(int photoId)
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        if (companyId == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var fileKey = await _mediaRepository.DeletePhotoAsync(companyId.Value, photoId, HttpContext.RequestAborted);
        if (fileKey == null)
        {
            return NotFound(new { message = CompanyMedia.PhotoNotFoundMessage });
        }

        await _fileStorage.DeleteQuietlyAsync(fileKey, _logger);
        return NoContent();
    }

    // Obraz tła ofert: multipart/form-data z jednym polem "file"; zastępuje poprzedni obraz (jego plik jest usuwany po
    // zapisaniu zmiany). Przyciemnienie i punkt zaczepienia zostają bez zmian. Odpowiedź: { url, dim, focus }.
    [HttpPut("offer-background")]
    [EnableRateLimiting(RateLimitingSettings.UploadsPolicy)]
    [RequestSizeLimit(AttachmentFiles.MaxRequestSize)]
    [DisableFormValueModelBinding]
    [ProducesResponseType(typeof(OfferBackgroundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<OfferBackgroundDto>> UploadOfferBackground()
    {
        var cancellationToken = HttpContext.RequestAborted;

        // firma ustalana PRZED odczytem treści żądania
        var companyId = await _access.GetEmployerCompanyIdAsync();
        if (companyId == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var form = await CompanyMedia.ReadBackgroundAsync(HttpContext, cancellationToken);
        if (form.Error != null)
        {
            return BadRequest(new { message = form.Error });
        }

        var file = form.Files[0];
        var validation = await ImageFiles.ValidateAsync(file, cancellationToken);
        if (validation.Image == null)
        {
            return BadRequest(new { message = validation.Error });
        }

        var type = validation.Image.Type;
        var fileKey = CompanyMedia.NewBackgroundStorageKey(companyId.Value, type);
        var recordMayExist = false;
        BackgroundChange change;
        try
        {
            await using (var content = file.OpenReadStream())
            {
                await _fileStorage.SaveAsync(fileKey, content, type.ContentType, cancellationToken);
            }

            // bez tokenu żądania - jak przy zdjęciach
            recordMayExist = true;
            change = await _mediaRepository.SetOfferBackgroundAsync(companyId.Value, fileKey, type.ContentType,
                CancellationToken.None);
        }
        catch
        {
            if (!recordMayExist || await IsBackgroundMissingAsync(companyId.Value, fileKey))
            {
                await _fileStorage.DeleteQuietlyAsync(fileKey, _logger);
            }

            throw;
        }

        if (!change.Found)
        {
            await _fileStorage.DeleteQuietlyAsync(fileKey, _logger);
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        await _fileStorage.DeleteQuietlyAsync(change.PreviousKey, _logger);

        var company = await _mediaRepository.FindCompanyAsync(companyId.Value, CancellationToken.None);
        if (company == null)
        {
            // firmę usunięto zaraz po zapisie - jej pliki usunęło to samo usunięcie
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        return Ok(company.ToOfferBackgroundDto(allowWithoutImage: true));
    }

    // Przyciemnienie (55-95) i punkt zaczepienia tła; działa także przed dodaniem obrazu (url = null)
    [HttpPatch("offer-background")]
    [ProducesResponseType(typeof(OfferBackgroundDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OfferBackgroundDto>> UpdateOfferBackground(UpdateOfferBackgroundDto backgroundDto)
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        var cancellationToken = HttpContext.RequestAborted;
        if (companyId == null
            || !await _mediaRepository.UpdateOfferBackgroundSettingsAsync(companyId.Value, backgroundDto.Dim, backgroundDto.Focus,
                cancellationToken))
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var company = await _mediaRepository.FindCompanyAsync(companyId.Value, cancellationToken);
        if (company == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        return Ok(company.ToOfferBackgroundDto(allowWithoutImage: true));
    }

    // Usunięcie obrazu tła (oferty wracają do zwykłego nagłówka); bez obrazu - także 204
    [HttpDelete("offer-background")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteOfferBackground()
    {
        var companyId = await _access.GetEmployerCompanyIdAsync();
        if (companyId == null)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        var change = await _mediaRepository.SetOfferBackgroundAsync(companyId.Value, null, null, HttpContext.RequestAborted);
        if (!change.Found)
        {
            return NotFound(new { message = CompanyMessages.NotFound });
        }

        await _fileStorage.DeleteQuietlyAsync(change.PreviousKey, _logger);
        return NoContent();
    }

    // Sprzątanie po błędzie zapisu rekordów zdjęć: wyjątek mógł wystąpić już po zatwierdzeniu transakcji, więc
    // usuwane są tylko pliki bez rekordów; gdy nie da się tego sprawdzić, pliki zostają (jak przy załącznikach)
    private async Task DeletePhotoFilesWithoutRecordsAsync(IReadOnlyList<string> savedKeys)
    {
        IReadOnlyList<string> recorded;
        try
        {
            using var timeout = new CancellationTokenSource(FileStorageExtensions.CleanupTimeout);
            recorded = await _mediaRepository.GetExistingPhotoFileKeysAsync(savedKeys, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check which of {Count} saved company photo files have records - the files are kept",
                savedKeys.Count);
            return;
        }

        await _fileStorage.DeleteQuietlyAsync(savedKeys.Except(recorded, StringComparer.Ordinal), _logger);
    }

    // true - obraz tła na pewno nie został zapisany w bazie (plik można usunąć); błąd sprawdzenia - plik zostaje
    private async Task<bool> IsBackgroundMissingAsync(int companyId, string fileKey)
    {
        try
        {
            using var timeout = new CancellationTokenSource(FileStorageExtensions.CleanupTimeout);
            return !await _mediaRepository.IsOfferBackgroundKeyRecordedAsync(companyId, fileKey, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check whether offer background file {FileKey} has a record - the file is kept", fileKey);
            return false;
        }
    }
}
