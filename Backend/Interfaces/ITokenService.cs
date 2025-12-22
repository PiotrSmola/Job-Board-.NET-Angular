using Backend.Models;

namespace Backend.Interfaces;

public interface ITokenService
{
    string CreateToken(ApplicationUser user, IList<string> roles);
}
