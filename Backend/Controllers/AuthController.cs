using Backend.DTOs;
using Backend.Interfaces;
using Backend.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace Backend.Controllers;

[Route("api/[controller]")]
[ApiController]
public class AuthController : ControllerBase
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly ITokenService _tokenService;

    public AuthController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        ITokenService tokenService)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _tokenService = tokenService;
    }

    [HttpPost("register")]
    public async Task<ActionResult<UserDto>> Register(RegisterDto registerDto)
    {
        if (await _userManager.FindByEmailAsync(registerDto.Email) != null)
        {
            return BadRequest(new { message = "Email is already taken" });
        }

        var user = new ApplicationUser
        {
            UserName = registerDto.Email,
            Email = registerDto.Email,
            FirstName = registerDto.FirstName,
            LastName = registerDto.LastName
        };

        var result = await _userManager.CreateAsync(user, registerDto.Password);

        if (!result.Succeeded)
        {
            return BadRequest(result.Errors);
        }

        // Assign default role "User"
        await _userManager.AddToRoleAsync(user, "User");

        var roles = await _userManager.GetRolesAsync(user);

        return new UserDto
        {
            Email = user.Email ?? string.Empty,
            FirstName = user.FirstName ?? string.Empty,
            LastName = user.LastName ?? string.Empty,
            Token = _tokenService.CreateToken(user, roles)
        };
    }

    [HttpPost("login")]
    public async Task<ActionResult<UserDto>> Login(LoginDto loginDto)
    {
        var user = await _userManager.FindByEmailAsync(loginDto.Email);

        if (user == null)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

        var result = await _signInManager.CheckPasswordSignInAsync(user, loginDto.Password, false);

        if (!result.Succeeded)
        {
            return Unauthorized(new { message = "Invalid email or password" });
        }

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
