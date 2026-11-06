using Backend.Data;
using Backend.DTOs;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Backend.Settings;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Backend.Controllers;

// Cały kontroler pod polityką "auth" (limit per IP i wspólny limit równoległych żądań) - każdy nowy endpoint
// uwierzytelniania (np. reset hasła) dziedziczy ją automatycznie; poza tym kontrolerem użyj tego samego atrybutu
[Route("api/[controller]")]
[ApiController]
[EnableRateLimiting(RateLimitingSettings.AuthPolicy)]
public class AuthController : ControllerBase
{
    private const string EmailTakenMessage = "Email is already taken";

    private readonly UserManager<ApplicationUser> _userManager;
    private readonly ILoginService _loginService;
    private readonly ITokenService _tokenService;
    private readonly ICompanyRepository _companyRepository;
    private readonly ApplicationDbContext _context;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        ILoginService loginService,
        ITokenService tokenService,
        ICompanyRepository companyRepository,
        ApplicationDbContext context)
    {
        _userManager = userManager;
        _loginService = loginService;
        _tokenService = tokenService;
        _companyRepository = companyRepository;
        _context = context;
    }

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

        var (result, error) = await CreateUserAsync(user, registerDto.Password);
        if (error != null)
        {
            return error;
        }

        // default role "User"
        await _userManager.AddToRoleAsync(user, Roles.User);

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

        await transaction.CommitAsync();

        return await ToUserDtoAsync(user);
    }

    [HttpPost("login")]
    [ProducesResponseType(typeof(UserDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status423Locked)]
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var result = await _loginService.CheckCredentialsAsync(loginDto.Email, loginDto.Password, HttpContext.RequestAborted);

        return result.Status switch
        {
            LoginStatus.Succeeded => await ToUserDtoAsync(result.User!),
            LoginStatus.LockedOut => StatusCode(StatusCodes.Status423Locked,
                new { message = "Account is temporarily locked due to too many failed login attempts" }),
            _ => Unauthorized(new { message = "Invalid email or password" })
        };
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
            Token = _tokenService.CreateToken(user, roles)
        };
    }
}
