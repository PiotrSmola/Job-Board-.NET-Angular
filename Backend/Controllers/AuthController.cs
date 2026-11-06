using System.Security.Claims;
using Backend.Data;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Backend.Services.Email;
using Backend.Settings;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private const string EmailTakenMessage = "Email is already taken";
    public const string InvalidConfirmationLinkMessage = "Invalid or expired confirmation link";
    public const string EmailAlreadyConfirmedMessage = "Email is already confirmed";
    public const string InvalidResetLinkMessage = "Invalid or expired reset link";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;
    private readonly ICompanyRepository _companyRepository;
    private readonly ApplicationDbContext _context;
    private readonly IEmailOutbox _emailOutbox;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService,
        ICompanyRepository companyRepository,
        ApplicationDbContext context,
        IEmailOutbox emailOutbox,
        ILogger<AuthController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
        _companyRepository = companyRepository;
        _context = context;
        _emailOutbox = emailOutbox;
        _logger = logger;
    }

    // Rejestracja kandydata: konto z rolą User i e-mail z linkiem potwierdzającym - w jednej transakcji
    // (niepotwierdzony adres nie blokuje korzystania z aplikacji)
    [HttpPost("register")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> Register(RegisterDto registerDto)
    {
        if (await _userManager.FindByEmailAsync(registerDto.Email) != null)
        {
            return EmailTaken();
        }

        var user = new ApplicationUser
        {
            UserName = registerDto.Email,
            Email = registerDto.Email,
            FirstName = registerDto.FirstName,
            LastName = registerDto.LastName
        };

        // UserManager korzysta z tego samego (scoped) kontekstu - konto, rola i e-mail zapisują się w tej transakcji
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var (_, error) = await CreateUserAsync(user, registerDto.Password);
        if (error != null)
        {
            return error;
        }

        var roleResult = await _userManager.AddToRoleAsync(user, Roles.User);
        if (!roleResult.Succeeded)
        {
            return BadRequest(roleResult.Errors);
        }

        await _emailOutbox.QueueEmailConfirmationAsync(user);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToUserDtoAsync(user);
    }

    // Rejestracja pracodawcy: konto z rolą Employer i firma oczekująca na weryfikację - w jednej transakcji
    [HttpPost("register-employer")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<UserDto>> RegisterEmployer(RegisterEmployerDto registerDto)
    {
        if (await _userManager.FindByEmailAsync(registerDto.Email) != null)
        {
            return EmailTaken();
        }

        var companyName = CompanyNames.Normalize(registerDto.CompanyName);
        if (await _companyRepository.IsNameTakenAsync(companyName))
        {
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        // UserManager korzysta z tego samego (scoped) kontekstu - firma i konto zapisują się w tej transakcji
        await using var transaction = await _context.Database.BeginTransactionAsync();

        var company = new Company
        {
            Name = companyName,
            Description = CompanyNames.NormalizeOptional(registerDto.CompanyDescription),
            Website = CompanyNames.NormalizeOptional(registerDto.Website),
            Location = CompanyNames.NormalizeOptional(registerDto.Location),
            Status = CompanyStatus.Pending,
            CreatedAt = DateTime.UtcNow
        };

        try
        {
            await _companyRepository.AddCompanyAsync(company);
        }
        catch (Exception ex) when (ex.IsDuplicateKey())
        {
            // równoległa rejestracja zajęła nazwę (unikalny indeks)
            return BadRequest(new { message = CompanyMessages.NameTaken });
        }

        var user = new ApplicationUser
        {
            UserName = registerDto.Email,
            Email = registerDto.Email,
            FirstName = registerDto.FirstName,
            LastName = registerDto.LastName,
            CompanyId = company.Id
        };

        // wycofanie transakcji (dispose bez commit) usuwa też utworzoną firmę
        var (_, error) = await CreateUserAsync(user, registerDto.Password);
        if (error != null)
        {
            return error;
        }

        var roleResult = await _userManager.AddToRoleAsync(user, Roles.Employer);
        if (!roleResult.Succeeded)
        {
            return BadRequest(roleResult.Errors);
        }

        await _emailOutbox.QueueEmailConfirmationAsync(user);
        await _context.SaveChangesAsync();
        await transaction.CommitAsync();

        return await ToUserDtoAsync(user);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status423Locked)]
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var user = await _userManager.FindByEmailAsync(loginDto.Email);

        if (user == null)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, loginDto.Password, lockoutOnFailure: true);

        if (result.IsLockedOut)
        {
            return StatusCode(StatusCodes.Status423Locked,
                new { message = "Account is temporarily locked due to too many failed login attempts" });
        }

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        return await ToUserDtoAsync(user);
    }

    // Potwierdzenie adresu z linku z e-maila (POST - skanery linków w poczcie go nie wyzwolą); ponowne potwierdzenie
    // ważnym linkiem też kończy się sukcesem. Nie zmienia security stampu - wydane tokeny JWT zostają ważne.
    [HttpPost("confirm-email")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailDto confirmDto)
    {
        var user = await _userManager.FindByIdAsync(confirmDto.UserId);
        if (user == null || !EmailTokens.TryDecode(confirmDto.Token, out var token))
        {
            return BadRequest(new { message = InvalidConfirmationLinkMessage });
        }

        var result = await _userManager.ConfirmEmailAsync(user, token);
        if (!result.Succeeded)
        {
            return BadRequest(new { message = InvalidConfirmationLinkMessage });
        }

        return NoContent();
    }

    // Ponowne wysłanie linku potwierdzającego zalogowanemu użytkownikowi (limit żądań per użytkownik)
    [HttpPost("resend-confirmation")]
    [Authorize]
    [EnableRateLimiting(RateLimitingSettings.AccountEmailsPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ResendConfirmation()
    {
        var user = await CurrentUserAsync();
        if (user == null)
        {
            return Unauthorized();
        }

        if (user.EmailConfirmed)
        {
            return BadRequest(new { message = EmailAlreadyConfirmedMessage });
        }

        await _emailOutbox.QueueEmailConfirmationAsync(user);
        await _context.SaveChangesAsync();

        return NoContent();
    }

    // Link do ustawienia nowego hasła (ważny 1 h). Zawsze 202 - odpowiedź nie zdradza, czy konto istnieje; limit żądań
    // per adres IP i najwyżej 3 takie e-maile na konto w ciągu godziny.
    [HttpPost("forgot-password")]
    [EnableRateLimiting(RateLimitingSettings.AccountEmailsPolicy)]
    [ProducesResponseType(StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordDto forgotDto)
    {
        var user = await _userManager.FindByEmailAsync(forgotDto.Email);
        if (user != null)
        {
            if (await _emailOutbox.QueuePasswordResetAsync(user))
            {
                await _context.SaveChangesAsync();
            }
            else
            {
                _logger.LogInformation("Password reset e-mail limit reached for user {UserId} - no e-mail queued", user.Id);
            }
        }

        return Accepted();
    }

    // Nowe hasło z linku z e-maila. Zmienia security stamp (wydane tokeny JWT i inne linki resetu tracą ważność),
    // potwierdza adres (link przyszedł na ten adres) i zdejmuje blokadę po nieudanych logowaniach.
    [HttpPost("reset-password")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> ResetPassword(ResetPasswordDto resetDto)
    {
        var user = await _userManager.FindByEmailAsync(resetDto.Email);
        if (user == null || !EmailTokens.TryDecode(resetDto.Token, out var token))
        {
            return BadRequest(new { message = InvalidResetLinkMessage });
        }

        var result = await _userManager.ResetPasswordAsync(user, token, resetDto.NewPassword);
        if (!result.Succeeded)
        {
            // token jest sprawdzany przed hasłem - pozostałe błędy dotyczą siły nowego hasła
            if (result.Errors.Any(e => e.Code == nameof(IdentityErrorDescriber.InvalidToken)))
            {
                return BadRequest(new { message = InvalidResetLinkMessage });
            }

            return PasswordValidationProblem(nameof(ResetPasswordDto.NewPassword), result);
        }

        if (!user.EmailConfirmed)
        {
            user.EmailConfirmed = true;
            await _userManager.UpdateAsync(user);
        }

        await _userManager.ResetAccessFailedCountAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, null);

        return NoContent();
    }

    // Błędy siły hasła z Identity jako ValidationProblem (domyślne opisy IdentityErrorDescriber - frontend je tłumaczy)
    private ActionResult PasswordValidationProblem(string key, IdentityResult result)
    {
        foreach (var error in result.Errors)
        {
            ModelState.AddModelError(key, error.Description);
        }

        return ValidationProblem(ModelState);
    }

    private async Task<ApplicationUser?> CurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return string.IsNullOrEmpty(userId) ? null : await _userManager.FindByIdAsync(userId);
    }

    private BadRequestObjectResult EmailTaken() => BadRequest(new { message = EmailTakenMessage });

    // Tworzy konto; zajęty e-mail zgłoszony przez walidator Identity albo przez unikalny indeks
    // (równoległa rejestracja na ten sam adres wygrała wyścig z FindByEmailAsync) daje 400 zamiast 500
    private async Task<(IdentityResult? Result, ActionResult? Error)> CreateUserAsync(ApplicationUser user, string password)
    {
        IdentityResult result;
        try
        {
            result = await _userManager.CreateAsync(user, password);
        }
        catch (Exception ex) when (ex.IsDuplicateKey() || ex.IsConcurrentChangeConflict())
        {
            // duplikat klucza albo (obronnie) deadlock z równoległym INSERT tego samego konta - zapis wycofany
            return (null, EmailTaken());
        }

        if (result.Succeeded)
        {
            return (result, null);
        }

        var duplicate = result.Errors.Any(e =>
            e.Code is nameof(IdentityErrorDescriber.DuplicateEmail) or nameof(IdentityErrorDescriber.DuplicateUserName));
        return (result, duplicate ? EmailTaken() : BadRequest(result.Errors));
    }

    private async Task<UserDto> ToUserDtoAsync(ApplicationUser user)
    {
        var roles = await _userManager.GetRolesAsync(user);

        return new UserDto
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            Token = _tokenService.CreateToken(user, roles),
            EmailConfirmed = user.EmailConfirmed
        };
    }
}
