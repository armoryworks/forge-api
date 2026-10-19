using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.IdentityModel.Tokens;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// Customer-portal session token. A portal contact is not an employee: the
/// token is validated only by the dedicated <see cref="SchemeName"/> bearer
/// scheme, carries its own audience, and is signed with a key DERIVED from
/// the main JWT key, so it is never accepted by the employee pipeline (and an
/// employee token is never accepted on the portal endpoints). The subject is
/// <c>portal-contact:{contactId}</c>, which no employee lookup can resolve.
/// </summary>
public interface IPortalTokenService
{
    /// <summary>Mint a portal session token for a contact of a customer.</summary>
    TokenResult Issue(int contactId, int customerId, string email, string firstName, string lastName, TimeSpan lifetime);
}

public class PortalTokenService(IConfiguration config, IClock clock) : IPortalTokenService
{
    public const string SchemeName = "Portal";
    public const string Audience = "forge-portal";
    public const string SessionClaim = "portal_session";
    public const string CustomerIdClaim = "customer_id";
    public const string ContactIdClaim = "contact_id";
    public const string PortalRole = "PortalUser";

    public TokenResult Issue(int contactId, int customerId, string email, string firstName, string lastName, TimeSpan lifetime)
    {
        var jti = Guid.NewGuid().ToString();
        var expiresAt = clock.UtcNow.Add(lifetime);
        var initials = $"{firstName.FirstOrDefault()}{lastName.FirstOrDefault()}".ToUpperInvariant();

        var token = new JwtSecurityToken(
            issuer: Issuer(config),
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Jti, jti),
                new Claim(ClaimTypes.NameIdentifier, $"portal-contact:{contactId}"),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.GivenName, firstName),
                new Claim(ClaimTypes.Surname, lastName),
                new Claim("initials", initials),
                new Claim(ClaimTypes.Role, PortalRole),
                new Claim(SessionClaim, "true"),
                new Claim(CustomerIdClaim, customerId.ToString()),
                new Claim(ContactIdClaim, contactId.ToString()),
            ],
            expires: expiresAt.UtcDateTime,
            signingCredentials: new SigningCredentials(SigningKey(config), SecurityAlgorithms.HmacSha256));

        return new TokenResult(new JwtSecurityTokenHandler().WriteToken(token), jti, expiresAt);
    }

    /// <summary>Validation parameters for the <see cref="SchemeName"/> bearer scheme.</summary>
    public static TokenValidationParameters CreateValidationParameters(IConfiguration config) => new()
    {
        ValidateIssuer = true,
        ValidIssuer = Issuer(config),
        ValidateAudience = true,
        ValidAudience = Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = SigningKey(config),
    };

    private static string Issuer(IConfiguration config) => config["Jwt:Issuer"] ?? "forge";

    private static SymmetricSecurityKey SigningKey(IConfiguration config)
    {
        var baseKey = config["Jwt:Key"]
            ?? throw new InvalidOperationException("Jwt:Key is required to sign portal session tokens.");
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(baseKey));
        return new SymmetricSecurityKey(hmac.ComputeHash(Encoding.UTF8.GetBytes("forge:portal-session-token:v1")));
    }
}
