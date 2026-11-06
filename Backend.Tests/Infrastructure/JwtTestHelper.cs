using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend.Services;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Tests.Infrastructure;

public static class JwtTestHelper
{
    public static JwtSecurityToken Read(string token) => new JwtSecurityTokenHandler().ReadJwtToken(token);

    public static IReadOnlyList<string> GetRoles(string token) =>
        Read(token).Claims
            .Where(c => c.Type == "role" || c.Type == ClaimTypes.Role)
            .Select(c => c.Value)
            .ToList();

    public static string GetUserId(string token) =>
        Read(token).Claims
            .First(c => c.Type == ClaimTypes.NameIdentifier || c.Type == "nameid")
            .Value;

    // wartość claimu security stampu z tokenu (skrót stampu - TokenService.HashSecurityStamp)
    public static string? GetSecurityStamp(string token) =>
        Read(token).Claims.FirstOrDefault(c => c.Type == TokenService.SecurityStampClaimType)?.Value;

    // Buduje token podpisany dowolnym kluczem - do testów odrzucania nieprawidłowych tokenów.
    // Token jest akceptowany tylko dla istniejącego użytkownika ze skrótem aktualnego security stampu (securityStamp
    // to wartość claimu, np. z GetSecurityStamp albo TokenService.HashSecurityStamp(stamp z bazy)).
    public static string Create(
        string secret,
        string issuer = "JobBoardAPI",
        string audience = "JobBoardClient",
        DateTime? expires = null,
        string userId = "some-user-id",
        string? role = "Admin",
        string? securityStamp = null)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, userId) };
        if (securityStamp != null)
        {
            claims.Add(new Claim(TokenService.SecurityStampClaimType, securityStamp));
        }

        if (role != null)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secret));
        var now = DateTime.UtcNow;
        var descriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            NotBefore = expires.HasValue ? expires.Value.AddHours(-2) : now,
            Expires = expires ?? now.AddHours(1),
            Issuer = issuer,
            Audience = audience,
            SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256)
        };

        var handler = new JwtSecurityTokenHandler();
        return handler.WriteToken(handler.CreateToken(descriptor));
    }
}
