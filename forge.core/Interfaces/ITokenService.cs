using System.Security.Claims;

namespace Forge.Core.Interfaces;

public record TokenResult(string Token, string Jti, DateTimeOffset ExpiresAt);

public interface ITokenService
{
    TokenResult GenerateToken(int userId, string email, string firstName, string lastName,
        string? initials, string? avatarColor, IList<string> roles,
        TimeSpan? expiry = null, IDictionary<string, string>? extraClaims = null);
}
