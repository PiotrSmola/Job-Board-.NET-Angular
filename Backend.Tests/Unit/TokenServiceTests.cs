using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Backend.Models;
using Backend.Services;
using Backend.Settings;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace Backend.Tests.Unit;

public class TokenServiceTests
{
    private const string Secret = "unit-test-signing-key-0123456789-abcdefghij";

    private static TokenService CreateService(int expirationMinutes = 60) =>
        new(Options.Create(new JwtSettings { Secret = Secret, ExpirationInMinutes = expirationMinutes }));

    private static ApplicationUser NewUser() => new()
    {
        Id = "user-123",
        Email = "jan@example.com",
        FirstName = "Jan",
        LastName = "Kowalski"
    };

    [Fact]
    public void CreateToken_ContainsIdentityClaimsAndRoles()
    {
        var token = CreateService().CreateToken(NewUser(), new List<string> { "User", "Admin" });

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == "nameid" && c.Value == "user-123");
        Assert.Contains(jwt.Claims, c => c.Type == "email" && c.Value == "jan@example.com");
        Assert.Contains(jwt.Claims, c => c.Type == "unique_name" && c.Value == "Jan Kowalski");
        Assert.Equal(new[] { "User", "Admin" }, jwt.Claims.Where(c => c.Type == "role").Select(c => c.Value).ToArray());
    }

    [Fact]
    public void CreateToken_ContainsUserSecurityStamp()
    {
        var user = NewUser();
        user.SecurityStamp = "stamp-abc";

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(CreateService().CreateToken(user, new List<string>()));

        Assert.Equal("AspNet.Identity.SecurityStamp", TokenService.SecurityStampClaimType);
        Assert.Contains(jwt.Claims, c => c.Type == TokenService.SecurityStampClaimType && c.Value == "stamp-abc");
    }

    [Fact]
    public void CreateToken_WithoutSecurityStamp_OmitsStampClaim()
    {
        var user = NewUser();
        user.SecurityStamp = null;

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(CreateService().CreateToken(user, new List<string>()));

        Assert.DoesNotContain(jwt.Claims, c => c.Type == TokenService.SecurityStampClaimType);
    }

    [Fact]
    public void CreateToken_WithoutRoles_HasNoRoleClaims()
    {
        var token = CreateService().CreateToken(NewUser(), new List<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.DoesNotContain(jwt.Claims, c => c.Type == "role");
    }

    [Fact]
    public void CreateToken_UsesConfiguredIssuerAudienceAndExpiry()
    {
        var service = new TokenService(Options.Create(new JwtSettings
        {
            Secret = Secret, Issuer = "MyIssuer", Audience = "MyAudience", ExpirationInMinutes = 30
        }));

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(service.CreateToken(NewUser(), new List<string>()));

        Assert.Equal("MyIssuer", jwt.Issuer);
        Assert.Contains("MyAudience", jwt.Audiences);
        Assert.InRange((jwt.ValidTo - DateTime.UtcNow).TotalMinutes, 29, 30.5);
    }

    [Fact]
    public void CreateToken_IsSignedWithHmacSha256AndValidatesWithSameSecret()
    {
        var token = CreateService().CreateToken(NewUser(), new List<string> { "User" });
        var parameters = new TokenValidationParameters
        {
            ValidIssuer = "JobBoardAPI",
            ValidAudience = "JobBoardClient",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ClockSkew = TimeSpan.Zero
        };

        var principal = new JwtSecurityTokenHandler().ValidateToken(token, parameters, out var validated);

        Assert.Equal("HS256", ((JwtSecurityToken)validated).Header.Alg);
        Assert.True(principal.IsInRole("User"));
        Assert.Equal("user-123", principal.FindFirstValue(ClaimTypes.NameIdentifier));
    }

    [Fact]
    public void CreateToken_FailsValidationWithDifferentSecret()
    {
        var token = CreateService().CreateToken(NewUser(), new List<string>());
        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = false,
            ValidateAudience = false,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes("another-secret-another-secret-another-secret"))
        };

        Assert.ThrowsAny<SecurityTokenException>(() => new JwtSecurityTokenHandler().ValidateToken(token, parameters, out _));
    }

    [Fact]
    public void CreateToken_WithMissingEmailAndNames_DoesNotThrow()
    {
        var user = new ApplicationUser { Id = "id-only" };

        var token = CreateService().CreateToken(user, new List<string>());

        var jwt = new JwtSecurityTokenHandler().ReadJwtToken(token);
        Assert.Contains(jwt.Claims, c => c.Type == "nameid" && c.Value == "id-only");
    }
}
