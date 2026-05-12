using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Authentication;

/// <summary>
/// ASP.NET Core authentication handler for BI API keys.
///
/// Lookup strategy: keys are stored as Identity-hashed (PBKDF2 + per-row salt)
/// values, so the handler cannot reverse-lookup by hash. Instead it looks up
/// candidate rows by <c>KeyPrefix</c> (the first 12 characters of the
/// plaintext, persisted at issuance time), then verifies the presented
/// plaintext against each candidate's <c>KeyHash</c> via
/// <see cref="PasswordHasher{TUser}"/>. Prefix collisions are vanishingly
/// rare at 12 base64 characters but the loop handles them correctly.
///
/// Revoked, expired, or unknown keys produce an
/// <see cref="AuthenticateResult.Fail(string)"/> result so the framework
/// challenges with 401.
///
/// On successful auth, <c>LastUsedAt</c> is bumped (best-effort — failure
/// to persist does NOT fail the request). When configured, a
/// <c>BiApiKeyUsed</c> system-wide audit row is also emitted (off by default
/// to avoid log noise on high-volume read traffic).
///
/// Phase 3 / WU-04 / A3 / P0-AUTH-004.
/// </summary>
public class BiApiKeyAuthenticationHandler : AuthenticationHandler<BiApiKeyAuthenticationOptions>
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ISystemAuditWriter _auditWriter;
    private static readonly PasswordHasher<object> KeyHasher = new();

    // 12-char prefix preserved from the issuance flow (first 12 chars of the
    // plaintext, e.g. "qbe_abc12345"). Used as a coarse filter before the
    // PBKDF2 verify pass.
    private const int ExpectedPrefixLength = 12;

    public BiApiKeyAuthenticationHandler(
        IOptionsMonitor<BiApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db,
        IClock clock,
        ISystemAuditWriter auditWriter)
        : base(options, logger, encoder)
    {
        _db = db;
        _clock = clock;
        _auditWriter = auditWriter;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var presented = ExtractKey(Request);
        if (string.IsNullOrEmpty(presented))
            return AuthenticateResult.NoResult();

        if (presented.Length < ExpectedPrefixLength)
            return AuthenticateResult.Fail("API key too short to be valid.");

        var prefix = presented[..ExpectedPrefixLength];
        var now = _clock.UtcNow;

        // Pull all active, non-expired candidates that share the prefix.
        // KeyPrefix is indexed (BiApiKeyConfiguration.HasIndex) so this is cheap.
        var candidates = await _db.BiApiKeys
            .Where(k => k.KeyPrefix == prefix
                && k.IsActive
                && (k.ExpiresAt == null || k.ExpiresAt > now))
            .ToListAsync();

        if (candidates.Count == 0)
            return AuthenticateResult.Fail("API key not found, revoked, or expired.");

        BiApiKey? matched = null;
        foreach (var candidate in candidates)
        {
            var verifyResult = KeyHasher.VerifyHashedPassword(null!, candidate.KeyHash, presented);
            if (verifyResult == PasswordVerificationResult.Success
                || verifyResult == PasswordVerificationResult.SuccessRehashNeeded)
            {
                matched = candidate;
                break;
            }
        }

        if (matched == null)
            return AuthenticateResult.Fail("API key not found, revoked, or expired.");

        // Optional IP allow-list. We deliberately do NOT short-circuit before
        // PBKDF2 — that would leak whether a key exists at the prefix.
        if (!string.IsNullOrEmpty(matched.AllowedIpsJson))
        {
            var clientIp = Context.Connection.RemoteIpAddress?.ToString();
            var allowed = false;
            try
            {
                var list = System.Text.Json.JsonSerializer.Deserialize<List<string>>(matched.AllowedIpsJson);
                if (list == null || list.Count == 0)
                {
                    allowed = true;
                }
                else if (clientIp != null)
                {
                    foreach (var ip in list)
                    {
                        if (string.Equals(ip, clientIp, StringComparison.OrdinalIgnoreCase))
                        {
                            allowed = true;
                            break;
                        }
                    }
                }
            }
            catch
            {
                // Malformed allow-list JSON — treat as no constraint rather than
                // hard-failing every request; admin can fix the row.
                allowed = true;
            }

            if (!allowed)
                return AuthenticateResult.Fail("API key not permitted from this network address.");
        }

        // Best-effort LastUsedAt bump. We deliberately suppress failures here:
        // if the DB write fails (e.g. transient connection issue), the caller
        // should still be authenticated — they presented a valid key.
        try
        {
            matched.LastUsedAt = now;
            await _db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "BiApiKey {KeyId} authenticated but LastUsedAt update failed", matched.Id);
        }

        // Optional best-effort audit emission. UserId=0 because BiApiKey is
        // not bound to a user; entityType/entityId locate the key itself.
        if (Options.AuditUseEvents)
        {
            try
            {
                await _auditWriter.WriteAsync(
                    action: "BiApiKeyUsed",
                    userId: 0,
                    entityType: nameof(BiApiKey),
                    entityId: matched.Id,
                    details: $"{{\"keyPrefix\":\"{matched.KeyPrefix}\",\"name\":\"{EscapeJson(matched.Name)}\"}}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "BiApiKey {KeyId} authenticated but audit emission failed", matched.Id);
            }
        }

        // Build the principal. NameIdentifier is the BiApiKey id (synthetic;
        // BiApiKey is not bound to a user). Role "BiApiClient" lets endpoints
        // gate via [Authorize(Roles = "BiApiClient")].
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, matched.Id.ToString()),
            new(ClaimTypes.Name, $"BI Key: {matched.Name}"),
            new(ClaimTypes.Role, "BiApiClient"),
            new("bi_api_key_id", matched.Id.ToString()),
            new("bi_api_key_prefix", matched.KeyPrefix),
        };

        var identity = new ClaimsIdentity(claims, BiApiKeyAuthenticationOptions.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, BiApiKeyAuthenticationOptions.SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    /// <summary>
    /// Extracts the presented key from one of the supported headers.
    /// Returns null when no header is present.
    /// </summary>
    private static string? ExtractKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue(BiApiKeyAuthenticationOptions.HeaderName, out var headerValues))
        {
            var value = headerValues.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        if (request.Headers.TryGetValue("Authorization", out var authValues))
        {
            var auth = authValues.ToString();
            if (!string.IsNullOrWhiteSpace(auth)
                && auth.StartsWith(BiApiKeyAuthenticationOptions.AuthorizationScheme + " ",
                    StringComparison.OrdinalIgnoreCase))
            {
                var key = auth[(BiApiKeyAuthenticationOptions.AuthorizationScheme.Length + 1)..].Trim();
                if (!string.IsNullOrWhiteSpace(key))
                    return key;
            }
        }

        return null;
    }

    private static string EscapeJson(string value)
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        return value.Replace("\\", "\\\\").Replace("\"", "\\\"");
    }
}
