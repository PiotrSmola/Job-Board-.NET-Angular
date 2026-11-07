using Backend.Data;
using Backend.Helpers;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly RoleManager<IdentityRole> _roleManager;
    private readonly IWebHostEnvironment _environment;
    private readonly IConfiguration _configuration;
    private readonly ILogger<AdminController> _logger;
    private readonly IFileStorage _fileStorage;

    public AdminController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<AdminController> logger,
        IFileStorage fileStorage)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
        _fileStorage = fileStorage;
    }

    // Reset danych jest dostępny tylko w Development albo po jawnym włączeniu Admin:AllowSeedRefresh
    private bool IsSeedRefreshAllowed =>
        _environment.IsDevelopment() || _configuration.GetValue<bool>("Admin:AllowSeedRefresh");

    [HttpGet("settings")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public IActionResult GetSettings()
    {
        return Ok(new { allowSeedRefresh = IsSeedRefreshAllowed });
    }

    [HttpPost("refresh-seed")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(object), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(object), StatusCodes.Status500InternalServerError)]
    public async Task<IActionResult> RefreshSeed()
    {
        if (!IsSeedRefreshAllowed)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { message = "Seed refresh is disabled in this environment" });
        }

        try
        {
            _logger.LogWarning("Database seed refresh requested - all users, job offers and applications will be replaced");

            await DatabaseSeeder.RefreshSeedAsync(_context, _userManager, _roleManager,
                DatabaseSeeder.DemoAccountsEnabled(_environment, _configuration),
                DatabaseSeeder.AdminPassword(_environment, _configuration));

            _logger.LogInformation("Database seed refresh completed successfully");

            await DeleteAllAttachmentFilesAsync();

            return Ok(new
            {
                message = "Database has been refreshed with seed data",
                timestamp = DateTime.UtcNow
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while refreshing database seed");
            return StatusCode(StatusCodes.Status500InternalServerError, new
            {
                message = "An error occurred while refreshing seed data"
            });
        }
    }

    // Po zatwierdzonym resecie żadna aplikacja nie ma załączników - usuwane są wszystkie ich pliki (także osierocone);
    // błąd jest tylko logowany. Bez tokenu żądania (przerwane żądanie nie przerywa sprzątania po zatwierdzonym resecie),
    // ale z limitem czasu, żeby zawieszony magazyn nie blokował odpowiedzi w nieskończoność
    private async Task DeleteAllAttachmentFilesAsync()
    {
        // załączniki aplikacji i dokumenty profili - wszystkie rekordy zostały usunięte razem z użytkownikami; zdjęcia
        // i tła firm - razem z firmami
        foreach (var prefix in new[] { AttachmentFiles.StorageKeyPrefix, AttachmentFiles.ProfileStorageKeyPrefix, CompanyMedia.StorageKeyPrefix })
        {
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
                await _fileStorage.DeleteByPrefixAsync(prefix, timeout.Token);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete files with prefix {Prefix} from storage after the seed refresh", prefix);
            }
        }
    }
}
