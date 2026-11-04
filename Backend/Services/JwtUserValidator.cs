using System.Security.Claims;
using Backend.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

// Dodatkowa walidacja tokenu JWT: użytkownik musi nadal istnieć, a security stamp z tokenu
// musi zgadzać się z aktualnym (usunięcie konta lub zmiana stampu unieważnia wydane tokeny)
public static class JwtUserValidator
{
    public static async Task ValidateAsync(TokenValidatedContext context)
    {
        var userId = context.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrEmpty(userId))
        {
            context.Fail("Token does not contain a user identifier");
            return;
        }

        var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<ApplicationUser>>();
        var user = await userManager.Users
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.SecurityStamp })
            .FirstOrDefaultAsync(context.HttpContext.RequestAborted);

        if (user == null)
        {
            context.Fail("User no longer exists");
            return;
        }

        var tokenStamp = context.Principal!.FindFirstValue(TokenService.SecurityStampClaimType);
        if (!string.Equals(tokenStamp ?? string.Empty, user.SecurityStamp ?? string.Empty, StringComparison.Ordinal))
        {
            context.Fail("Token security stamp is no longer valid");
        }
    }
}
