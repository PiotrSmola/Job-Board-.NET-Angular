using Backend.Data;
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

    public AdminController(
        ApplicationDbContext context,
        UserManager<ApplicationUser> userManager,
        RoleManager<IdentityRole> roleManager,
        IWebHostEnvironment environment,
        IConfiguration configuration,
        ILogger<AdminController> logger)
    {
        _context = context;
        _userManager = userManager;
        _roleManager = roleManager;
        _environment = environment;
        _configuration = configuration;
        _logger = logger;
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

            await DatabaseSeeder.RefreshSeedAsync(_context, _userManager, _roleManager);

            _logger.LogInformation("Database seed refresh completed successfully");

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
}
