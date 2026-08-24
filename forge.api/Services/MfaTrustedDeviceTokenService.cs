using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

using Microsoft.IdentityModel.Tokens;

using Forge.Core.Interfaces;

namespace Forge.Api.Services;

/// <summary>
/// "Remember this device for 30 days" trusted-device token. Issued only after a
/// full MFA challenge succeeds with the remember option, and presented by the
/// client on subsequent logins to skip the MFA challenge (password still
/// required). Mirrors <see cref="MfaPreAuthTokenService"/>'s defensive design:
/// single-purpose (token_use=mfa_trusted_device, no roles), subject-bound
/// (userId read only from the token, never caller-supplied), signed with a key
/// DERIVED from the main JWT key so it is inert against every other token
/// pipeline. Differs only in TTL (30 days) and purpose.
/// </summary>
public class MfaTrustedDeviceTokenService(IConfiguration config) : IMfaTrustedDeviceTokenService
{
    private const string PurposeClaim = "token_use";
    private const string PurposeValue = "mfa_trusted_device";
    private const string Audience = "forge:mfa-trusted-device";
    private static readonly TimeSpan Ttl = TimeSpan.FromDays(30);

    private string Issuer => config["Jwt:Issuer"] ?? "forge";

    private SymmetricSecurityKey SigningKey
    {
        get
        {
            var baseKey = config["Jwt:Key"]
                ?? throw new InvalidOperationException("Jwt:Key is required to issue MFA trusted-device tokens.");
            using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(baseKey));
            var derived = hmac.ComputeHash(Encoding.UTF8.GetBytes("forge:mfa-trusted-device-token:v1"));
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

            if (principal.FindFirstValue(PurposeClaim) != PurposeValue)
                return null;

            var sub = principal.FindFirstValue(JwtRegisteredClaimNames.Sub)
                ?? principal.FindFirstValue(ClaimTypes.NameIdentifier);

            return int.TryParse(sub, out var userId) ? userId : null;
        }
        catch
        {
            return null;
        }
    }
}
