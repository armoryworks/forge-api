using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

using Microsoft.IdentityModel.Tokens;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

public class JwtTokenService(IConfiguration config) : ITokenService
{
    private static readonly TimeSpan DefaultExpiry = TimeSpan.FromHours(24);

    public TokenResult GenerateToken(int userId, string email, string firstName, string lastName,
        string? initials, string? avatarColor, IList<string> roles,
        TimeSpan? expiry = null, IDictionary<string, string>? extraClaims = null)
    {
        var jti = Guid.NewGuid().ToString();
        var expiresAt = DateTimeOffset.UtcNow.Add(expiry ?? DefaultExpiry);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Jti, jti),
            new(ClaimTypes.NameIdentifier, userId.ToString()),
            new(ClaimTypes.Email, email),
            new(ClaimTypes.GivenName, firstName),
            new(ClaimTypes.Surname, lastName),
        };

        if (initials is not null)
            claims.Add(new Claim("initials", initials));

        if (avatarColor is not null)
            claims.Add(new Claim("avatarColor", avatarColor));

        if (extraClaims is not null)
        {
            foreach (var (claimKey, claimValue) in extraClaims)
                claims.Add(new Claim(claimKey, claimValue));
        }

        foreach (var role in roles)
            claims.Add(new Claim(ClaimTypes.Role, role));

        // F-053: no committed fallback key — the signing key must be configured.
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is required to sign tokens.")));

        var token = new JwtSecurityToken(
            issuer: config["Jwt:Issuer"] ?? "forge",
            audience: config["Jwt:Audience"] ?? "forge-ui",
            claims: claims,
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt);
    }
}
