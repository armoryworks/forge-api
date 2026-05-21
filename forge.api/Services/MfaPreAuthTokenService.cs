using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.IdentityModel.Tokens;

namespace Forge.Api.Services;

/// <summary>
/// F-054 — MFA-pending pre-auth token. Issued by the login flow ONLY after a
/// successful password check for an MFA-enrolled user; it is the proof that the
/// first factor (password) was satisfied. The MFA challenge/validate/recovery
/// endpoints require and validate it before a full access JWT can be minted, so
/// MFA *supplements* the password instead of *replacing* it.
///
/// Design (maps to H-017 §A defensive requirements):
///   A1 single-purpose — carries a `token_use=mfa_pending` claim and NO role
///      claims; authorizes nothing except the MFA step.
///   A2 post-password   — only the login handler calls <see cref="Issue"/>, on
///      the success branch.
///   A3 subject-bound   — the userId is embedded in (and only read from) the
///      token; endpoints must never trust a caller-supplied userId.
///   A4 short TTL        — <see cref="Ttl"/> minutes, single challenge window.
///   A5 integrity        — signed (HS256) with a key DERIVED from the main JWT
///      key, so a pre-auth token is NOT accepted by the main bearer pipeline
///      (different signature) and a full access token is NOT accepted here.
/// </summary>
public interface IMfaPreAuthTokenService
{
    /// <summary>Mint an MFA-pending token bound to <paramref name="userId"/>.</summary>
    string Issue(int userId);

    /// <summary>Return the bound userId iff <paramref name="token"/> is a valid,
    /// unexpired, single-purpose MFA-pending token; otherwise null.</summary>
    int? ValidateAndGetUserId(string token);
}

public class MfaPreAuthTokenService(IConfiguration config) : IMfaPreAuthTokenService
{
    private const string PurposeClaim = "token_use";
    private const string PurposeValue = "mfa_pending";
    private const string Audience = "forge:mfa-pending";
    private static readonly TimeSpan Ttl = TimeSpan.FromMinutes(5);

    private string Issuer => config["Jwt:Issuer"] ?? "forge";

    // Derive a distinct signing key from the main JWT key so MFA-pending tokens
    // can never authenticate against the main bearer pipeline (and vice versa),
    // independent of how the main pipeline validates audience. No fallback —
    // absence of the key is a hard error (aligns with F-053).
    private SymmetricSecurityKey SigningKey
    {
        get
        {
            var baseKey = config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is required to issue MFA pre-auth tokens.");
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(baseKey));
            var derived = hmac.ComputeHash(Encoding.UTF8.GetBytes("forge:mfa-pending-token:v1"));
            return new SymmetricSecurityKey(derived);
        }
    }

    public string Issue(int userId)
    {
        var token = new JwtSecurityToken(
            issuer: Issuer,
            audience: Audience,
            claims:
            [
                new Claim(JwtRegisteredClaimNames.Sub, userId.ToString()),
                new Claim(PurposeClaim, PurposeValue),
                new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            ],
            expires: DateTime.UtcNow.Add(Ttl),
            signingCredentials: new SigningCredentials(SigningKey, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    public int? ValidateAndGetUserId(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        try
        {
            var principal = new JwtSecurityTokenHandler().ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = SigningKey,
                ValidateIssuer = true,
                ValidIssuer = Issuer,
                ValidateAudience = true,
                ValidAudience = Audience,
                ValidateLifetime = true,
                ClockSkew = TimeSpan.FromSeconds(30),
            }, out _);

            // Defense-in-depth: must be the single-purpose token, never a full JWT.
            if (principal.FindFirstValue(PurposeClaim) != PurposeValue)
                return null;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(sub, out var userId) ? userId : null;
        }
        catch
        {
            // Bad signature, wrong audience/issuer, expired, malformed → not valid.
            return null;
        }
    }
}
