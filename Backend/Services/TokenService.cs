using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Backend.Interfaces;
using Backend.Models;
using Backend.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Services;

public class TokenService : ITokenService
{
    // ta sama nazwa claimu co w ASP.NET Core Identity (ClaimsIdentityOptions.SecurityStampClaimType); wartością jest
    // skrót security stampu (HashSecurityStamp), nie sam stamp
    public const string SecurityStampClaimType = "AspNet.Identity.SecurityStamp";

    // liczba bajtów SHA-256 w skrócie stampu (128 bitów - wystarcza do wykrycia zmiany stampu)
    private const int StampHashBytes = 16;

    private readonly JwtSettings _jwtSettings;

    public TokenService(IOptions<JwtSettings> jwtSettings)
    {
        _jwtSettings = jwtSettings.Value;
    }

    public string CreateToken(ApplicationUser user, IList<string> roles)
    {
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim(ClaimTypes.Name, $"{user.FirstName} {user.LastName}")
        };

        // skrót security stampu pozwala unieważnić token po usunięciu konta lub zmianie stampu (JwtUserValidator);
        // sam stamp nie trafia do tokenu (payload JWT jest jawny - stamp to wewnętrzny sekret Identity)
        if (!string.IsNullOrEmpty(user.SecurityStamp))
        {
            claims.Add(new Claim(SecurityStampClaimType, HashSecurityStamp(user.SecurityStamp)));
        }

        foreach (var role in roles)
        {
            claims.Add(new Claim(ClaimTypes.Role, role));
        }

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwtSettings.Secret));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenDescriptor = new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(claims),
            Expires = DateTime.UtcNow.AddMinutes(_jwtSettings.ExpirationInMinutes),
            SigningCredentials = credentials,
            Issuer = _jwtSettings.Issuer,
            Audience = _jwtSettings.Audience
        };

        var tokenHandler = new JwtSecurityTokenHandler();
        var token = tokenHandler.CreateToken(tokenDescriptor);

        return tokenHandler.WriteToken(token);
    }

    // Base64url pierwszych 16 bajtów SHA-256 security stampu - wartość claimu SecurityStampClaimType
    public static string HashSecurityStamp(string securityStamp)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(securityStamp));
        return Base64UrlEncoder.Encode(hash, 0, StampHashBytes);
    }
}
