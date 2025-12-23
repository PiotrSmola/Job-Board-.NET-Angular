using Backend.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin")]
public class AdminController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<AdminController> _logger;

    public AdminController(IServiceProvider serviceProvider, ILogger<AdminController> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Resets all data (users, job offers, applications) and re-seeds from DatabaseSeeder
    /// Similar to Laravel's db:seed --refresh
    /// </summary>
    /// <returns></returns>
    [HttpPost("refresh-seed")]
    public async Task<IActionResult> RefreshSeed()
    {
        try
        {
            _logger.LogInformation("Starting database seed refresh...");

            await DatabaseSeeder.RefreshSeedAsync(_serviceProvider);

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
            return StatusCode(500, new
            {
                message = "An error occurred while refreshing seed data",
                error = ex.Message
            });
        }
    }
}
