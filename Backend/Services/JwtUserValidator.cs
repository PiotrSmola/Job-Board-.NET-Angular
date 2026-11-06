using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace Backend.Services;

// Dodatkowa walidacja tokenu JWT: użytkownik musi nadal istnieć, a skrót security stampu z tokenu
// musi zgadzać się ze skrótem aktualnego stampu (usunięcie konta lub zmiana stampu unieważnia wydane tokeny)
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

        var tokenStamp = context.Principal!.FindFirstValue(TokenService.SecurityStampClaimType) ?? string.Empty;
        var currentStamp = string.IsNullOrEmpty(user.SecurityStamp) ? string.Empty : TokenService.HashSecurityStamp(user.SecurityStamp);
        // porównanie w stałym czasie - czas odpowiedzi nie zdradza, ile znaków skrótu się zgadza
        if (!CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(tokenStamp), Encoding.UTF8.GetBytes(currentStamp)))
        {
            context.Fail("Token security stamp is no longer valid");
        }
    }
}
