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

namespace Backend.Controllers;

// Konto zalogowanego użytkownika: profil, hasło, usunięcie konta, dokumenty profilu (kandydat) i powiadomienia e-mail
[Route("api/account")]
[ApiController]
[Authorize]
public class AccountController : ControllerBase
{
    public const string CurrentPasswordIncorrectMessage = "Current password is incorrect";
    public const string AdminCannotBeDeletedMessage = "Administrator accounts cannot be deleted here";
    public const string LastCompanyMemberMessage = "You are the last member of a company with job offers";
    public const string LockedOutMessage = "Account is temporarily locked due to too many failed login attempts";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly IAccountRepository _accountRepository;
    private readonly IFileStorage _fileStorage;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        IAccountRepository accountRepository,
        IFileStorage fileStorage,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _accountRepository = accountRepository;
        _fileStorage = fileStorage;
        _logger = logger;
    }

    [HttpGet("profile")]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileDto>> GetProfile()
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        return Ok(user.ToProfileDto(await _userManager.GetRolesAsync(user)));
    }

    // Dane profilu (bez e-maila); wartości przycięte, puste pola opcjonalne zapisywane jako null
    [HttpPut("profile")]
    [ProducesResponseType(typeof(ProfileDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileDto>> UpdateProfile(UpdateProfileDto profileDto)
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        user.FirstName = profileDto.FirstName.Trim();
        user.LastName = profileDto.LastName.Trim();
        user.PhoneNumber = Optional(profileDto.PhoneNumber);
        user.Headline = Optional(profileDto.Headline);
        user.Location = Optional(profileDto.Location);
        user.LinkedInUrl = Optional(profileDto.LinkedInUrl);
        user.About = Optional(profileDto.About);

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(user.ToProfileDto(await _userManager.GetRolesAsync(user)));
    }

    // Zmiana hasła: nowy security stamp unieważnia wcześniej wydane tokeny - odpowiedź zawiera nowy token.
    // Błędne obecne hasło liczy się do blokady konta jak przy logowaniu.
    [HttpPost("change-password")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status423Locked)]
    public async Task<ActionResult<UserDto>> ChangePassword(ChangePasswordDto passwordDto)
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        var passwordCheck = await CheckPasswordAsync(user, passwordDto.CurrentPassword);
        if (passwordCheck != null)
        {
            return passwordCheck;
        }

        var result = await _userManager.ChangePasswordAsync(user, passwordDto.CurrentPassword, passwordDto.NewPassword);
        if (!result.Succeeded)
        {
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.PasswordMismatch)))
            {
                return BadRequest(new { message = CurrentPasswordIncorrectMessage });
            }

            foreach (var error in result.Errors)
            {
                ModelState.AddModelError(nameof(ChangePasswordDto.NewPassword), error.Description);
            }

            return ValidationProblem(ModelState);
        }

        var roles = await _userManager.GetRolesAsync(user);
        return Ok(new UserDto
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            Token = _tokenService.CreateToken(user, roles),
            EmailConfirmed = user.EmailConfirmed
        });
    }

    // Usunięcie konta (potwierdzone hasłem) z aplikacjami, załącznikami, dokumentami profilu i zapisanymi ofertami;
    // pliki z magazynu dopiero po zatwierdzeniu usunięcia w bazie. Konto admina - tylko przez innego admina w bazie.
    [HttpDelete]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status423Locked)]
    public async Task<IActionResult> DeleteAccount(DeleteAccountDto deleteDto)
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        if (await _userManager.IsInRoleAsync(user, Roles.Admin))
        {
            return BadRequest(new { message = AdminCannotBeDeletedMessage });
        }

        var passwordCheck = await CheckPasswordAsync(user, deleteDto.Password);
        if (passwordCheck != null)
        {
            return passwordCheck;
        }

        var deletion = await _accountRepository.DeleteAccountAsync(user.Id);
        switch (deletion.Status)
        {
            case AccountDeleteStatus.NotFound:
                return Unauthorized();
            case AccountDeleteStatus.LastCompanyMember:
                return BadRequest(new { message = LastCompanyMemberMessage });
        }

        _logger.LogInformation("Account {UserId} deleted by its owner ({FileCount} file(s) to delete)", user.Id, deletion.FileKeys.Count);
        await _fileStorage.DeleteQuietlyAsync(deletion.FileKeys, _logger);

        return NoContent();
    }

    [HttpGet("notifications")]
    [ProducesResponseType(typeof(NotificationPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationPreferencesDto>> GetNotificationPreferences()
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        return Ok(user.ToNotificationPreferencesDto());
    }

    // Wszystkie trzy pola wymagane (pola nieistotne dla roli frontend wysyła bez zmian)
    [HttpPut("notifications")]
    [ProducesResponseType(typeof(NotificationPreferencesDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<NotificationPreferencesDto>> UpdateNotificationPreferences(NotificationPreferencesDto preferencesDto)
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        user.NotifyApplicationStatusChanged = preferencesDto.ApplicationStatusChanged!.Value;
        user.NotifyNewApplication = preferencesDto.NewApplication!.Value;
        user.NotifyCompanyReview = preferencesDto.CompanyReview!.Value;

        var result = await _userManager.UpdateAsync(user);
        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        return Ok(user.ToNotificationPreferencesDto());
    }

    // --- dokumenty profilu (tylko kandydat) ---

    [HttpGet("documents")]
    [Authorize(Roles = Roles.User)]
    [ProducesResponseType(typeof(IEnumerable<ProfileDocumentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IEnumerable<ProfileDocumentDto>>> GetDocuments()
    {
        var documents = await _accountRepository.GetDocumentsAsync(UserId, HttpContext.RequestAborted);
        return Ok(documents.ToDtos());
    }

    // Dodanie dokumentów: multipart/form-data jak załączniki aplikacji (pola "files" i "kinds"); PDF, DOCX, PNG, JPEG
    // (typ po zawartości) do 5 MB na plik, najwyżej 10 dokumentów i 25 MB łącznie na profil. Wszystko albo nic:
    // walidacja wszystkich plików, zapis plików w magazynie, na końcu rekordy pod blokadą konta z kontrolą limitów.
    // Odpowiedź: 201 z pełną listą dokumentów profilu.
    [HttpPost("documents")]
    [Authorize(Roles = Roles.User)]
    [EnableRateLimiting(RateLimitingSettings.UploadsPolicy)]
    [RequestSizeLimit(AttachmentFiles.MaxRequestSize)]
    [DisableFormValueModelBinding]
    [ProducesResponseType(typeof(IEnumerable<ProfileDocumentDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<ActionResult<IEnumerable<ProfileDocumentDto>>> UploadDocuments()
    {
        var userId = UserId;
        var cancellationToken = HttpContext.RequestAborted;

        // pełny profil odrzucany lekkim zapytaniem PRZED odczytem treści żądania (do 26 MB)
        if (await _accountRepository.CountDocumentsAsync(userId, cancellationToken) >= AttachmentFiles.MaxProfileDocuments)
        {
            return BadRequest(new { message = AttachmentFiles.ProfileTooManyMessage });
        }

        var form = await AttachmentUploadForm.ReadAsync(HttpContext, AttachmentLimitMessages.Profile, cancellationToken);
        if (form.Error != null)
        {
            return BadRequest(new { message = form.Error });
        }

        var uploadedAt = DateTime.UtcNow;
        var uploads = new List<(IFormFile File, ProfileDocument Document)>(form.Items.Count);
        foreach (var item in form.Items)
        {
            var (error, type) = await AttachmentUploadForm.ValidateFileAsync(item.File, cancellationToken);
            if (type == null)
            {
                return BadRequest(new { message = error });
            }

            uploads.Add((item.File, new ProfileDocument
            {
                Kind = item.Kind,
                FileKey = AttachmentFiles.NewProfileStorageKey(userId, type),
                FileName = AttachmentFiles.SanitizeFileName(item.File.FileName, type),
                ContentType = type.ContentType,
                Size = item.File.Length,
                UploadedAt = uploadedAt
            }));
        }

        if (uploads.Sum(u => u.Document.Size) > AttachmentFiles.MaxProfileTotalSize)
        {
            return BadRequest(new { message = AttachmentFiles.ProfileTotalTooLargeMessage });
        }

        var savedKeys = new List<string>(uploads.Count);
        var recordsMayExist = false;
        DocumentsAddResult result;
        try
        {
            foreach (var (file, document) in uploads)
            {
                // klucz dodawany przed zapisem - sprzątany jest też plik, którego zapis się nie udał
                savedKeys.Add(document.FileKey);
                await using var content = file.OpenReadStream();
                await _fileStorage.SaveAsync(document.FileKey, content, document.ContentType, cancellationToken);
            }

            // bez tokenu żądania: po zapisaniu plików operacja jest doprowadzana do końca (jak przy załącznikach)
            recordsMayExist = true;
            result = await _accountRepository.AddDocumentsAsync(userId, uploads.Select(u => u.Document).ToList(),
                CancellationToken.None);
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

        if (result != DocumentsAddResult.Added)
        {
            // limit przekroczyło równoległe żądanie albo konto usunięto
            await _fileStorage.DeleteQuietlyAsync(savedKeys, _logger);
            return result switch
            {
                DocumentsAddResult.UserNotFound => Unauthorized(),
                DocumentsAddResult.TooMany => BadRequest(new { message = AttachmentFiles.ProfileTooManyMessage }),
                _ => BadRequest(new { message = AttachmentFiles.ProfileTotalTooLargeMessage })
            };
        }

        var documents = await _accountRepository.GetDocumentsAsync(userId, CancellationToken.None);
        return CreatedAtAction(nameof(GetDocuments), null, documents.ToDtos());
    }

    // Usunięcie dokumentu; plik z magazynu dopiero po usunięciu rekordu (dołączone wcześniej kopie w aplikacjach zostają)
    [HttpDelete("documents/{id:int}")]
    [Authorize(Roles = Roles.User)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DeleteDocument(int id)
    {
        var fileKey = await _accountRepository.DeleteDocumentAsync(UserId, id, HttpContext.RequestAborted);
        if (fileKey == null)
        {
            return NotFound(new { message = AttachmentFiles.DocumentNotFoundMessage });
        }

        await _fileStorage.DeleteQuietlyAsync(fileKey, _logger);
        return NoContent();
    }

    // Pobranie dokumentu (tylko właściciel - cudzy wygląda jak nieistniejący)
    [HttpGet("documents/{id:int}")]
    [Authorize(Roles = Roles.User)]
    [ProducesResponseType(typeof(FileStreamResult), StatusCodes.Status200OK, "application/pdf",
        "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "image/png", "image/jpeg")]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<IActionResult> DownloadDocument(int id)
    {
        var cancellationToken = HttpContext.RequestAborted;
        var document = await _accountRepository.GetDocumentAsync(UserId, id, cancellationToken);
        if (document == null)
        {
            return NotFound(new { message = AttachmentFiles.DocumentNotFoundMessage });
        }

        var file = await _fileStorage.OpenReadAsync(document.FileKey, cancellationToken);
        if (file == null)
        {
            _logger.LogWarning("Profile document file {FileKey} is missing in storage", document.FileKey);
            return NotFound(new { message = AttachmentFiles.DocumentNotFoundMessage });
        }

        return await this.StoredFileAsync(file, document.FileName, document.ContentType);
    }

    // [Authorize] gwarantuje uwierzytelnienie; JwtUserValidator - że konto nadal istnieje
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? string.Empty;

    private async Task<ApplicationUser?> CurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);
    }

    // Sprawdza hasło z licznikiem nieudanych prób (blokada konta jak przy logowaniu); null - hasło poprawne
    private async Task<ActionResult?> CheckPasswordAsync(ApplicationUser user, string password)
    {
        var result = await _signInManager.CheckPasswordSignInAsync(user, password, lockoutOnFailure: true);
        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked, new { message = LockedOutMessage });
        }

        return result.Succeeded ? null : BadRequest(new { message = CurrentPasswordIncorrectMessage });
    }

    // jak przy załącznikach: wyjątek mógł wystąpić po zatwierdzeniu transakcji - usuwane są tylko pliki bez rekordów
    private async Task DeleteFilesWithoutRecordsAsync(IReadOnlyList<string> savedKeys)
    {
        IReadOnlyList<string> recorded;
        try
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            recorded = await _accountRepository.GetExistingDocumentFileKeysAsync(savedKeys, timeout.Token);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not check which of {Count} saved profile document files have records - the files are kept",
                savedKeys.Count);
            return;
        }

        await _fileStorage.DeleteQuietlyAsync(savedKeys.Except(recorded, StringComparer.Ordinal), _logger);
    }

    private static string? Optional(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
