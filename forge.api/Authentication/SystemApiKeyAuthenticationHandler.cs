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
/// ASP.NET Core authentication handler for user-bound system API keys.
/// See <c>docs/api-key-integrations.md</c>.
///
/// Mirrors <see cref="BiApiKeyAuthenticationHandler"/>'s prefix-then-PBKDF2
/// lookup pattern. Differs in principal construction: on success, this
/// handler hydrates a principal AS the bound <see cref="ApplicationUser"/>:
///   - <c>NameIdentifier</c> = the user's id (so <c>AppDbContext.CurrentUserId</c>
///     picks it up via the standard middleware).
///   - <c>Email</c>, <c>Name</c> = the user's email / display name.
///   - <c>Role</c> claims = every role the user is granted via Identity
///     (looked up via <see cref="UserManager{T}.GetRolesAsync"/>).
///   - <c>system_api_key_id</c> / <c>system_api_key_prefix</c> = auxiliary
///     claims so audit / activity rows can distinguish a key-authenticated
///     request from a password-authenticated one.
///
/// The bound user's <c>IsActive</c> flag is honored — a deactivated user's
/// keys fail auth even if the key row itself is still active.
///
/// On successful auth, <c>SystemApiKey.LastUsedAt</c> is bumped best-effort
/// (a failed write does NOT fail the request). When configured, a
/// <c>SystemApiKeyUsed</c> system-wide audit row is also emitted (off by
/// default).
/// </summary>
public class SystemApiKeyAuthenticationHandler : AuthenticationHandler<SystemApiKeyAuthenticationOptions>
{
    private readonly AppDbContext _db;
    private readonly IClock _clock;
    private readonly ISystemAuditWriter _auditWriter;
    private readonly UserManager<ApplicationUser> _userManager;
    private static readonly PasswordHasher<object> KeyHasher = new();

    // 12-char prefix preserved from the issuance flow (first 12 chars of the
    // plaintext, e.g. "fsk_abc12345"). Used as a coarse filter before the
    // PBKDF2 verify pass.
    private const int ExpectedPrefixLength = 12;

    public SystemApiKeyAuthenticationHandler(
        IOptionsMonitor<SystemApiKeyAuthenticationOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AppDbContext db,
        IClock clock,
        ISystemAuditWriter auditWriter,
        UserManager<ApplicationUser> userManager)
        : base(options, logger, encoder)
    {
        _db = db;
        _clock = clock;
        _auditWriter = auditWriter;
        _userManager = userManager;
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
        // KeyPrefix is indexed (SystemApiKeyConfiguration.HasIndex).
        var candidates = await _db.SystemApiKeys
            .Where(k => k.KeyPrefix == prefix
                && k.IsActive
                && (k.ExpiresAt == null || k.ExpiresAt > now))
            .ToListAsync();

        if (candidates.Count == 0)
            return AuthenticateResult.Fail("API key not found, revoked, or expired.");

        SystemApiKey? matched = null;
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

        // Load the bound user. A deactivated user's keys fail auth even if
        // the key row itself is still active — preserves the "lock out a
        // person locks out their integrations" invariant.
        var user = await _db.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(u => u.Id == matched.UserId);

        if (user is null || !user.IsActive)
            return AuthenticateResult.Fail("Bound user not found or deactivated.");

        // Optional IP allow-list. Deliberately NOT short-circuited before
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
                // Malformed allow-list JSON — treat as no constraint rather
                // than hard-failing every request; admin can fix the row.
                allowed = true;
            }

            if (!allowed)
                return AuthenticateResult.Fail("API key not permitted from this network address.");
        }

        // Best-effort LastUsedAt bump. Suppress failures here: if the DB
        // write fails (e.g. transient connection issue), the caller should
        // still be authenticated — they presented a valid key.
        try
        {
            // Re-load the entity in a tracked state for the update. The
            // earlier query was untracked (.ToListAsync on the IQueryable —
            // tracked, but we want to keep the write small).
            var tracked = await _db.SystemApiKeys.FindAsync(matched.Id);
            if (tracked is not null)
            {
                tracked.LastUsedAt = now;
                await _db.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Logger.LogWarning(ex,
                "SystemApiKey {KeyId} authenticated but LastUsedAt update failed", matched.Id);
        }

        if (Options.AuditUseEvents)
        {
            try
            {
                await _auditWriter.WriteAsync(
                    action: "SystemApiKeyUsed",
                    userId: user.Id,
                    entityType: nameof(SystemApiKey),
                    entityId: matched.Id,
                    details: $"{{\"keyPrefix\":\"{matched.KeyPrefix}\",\"name\":\"{EscapeJson(matched.Name)}\"}}");
            }
            catch (Exception ex)
            {
                Logger.LogWarning(ex,
                    "SystemApiKey {KeyId} authenticated but audit emission failed", matched.Id);
            }
        }

        // Hydrate principal AS the bound user. NameIdentifier is the
        // user's id — this is what AppDbContext middleware reads to set
        // CurrentUserId, so audit / activity rows attribute to the system
        // user just like a normal interactive login.
        // UserManager.GetRolesAsync accepts an untracked ApplicationUser —
        // it goes through its own UserStore for the role lookup.
        var userRoles = (IEnumerable<string>)await _userManager.GetRolesAsync(user);

        // Per-key role-template scoping. When the key has a RoleTemplateId,
        // narrow the emitted role claims to the intersection of the user's
        // grants ∩ the template's IncludedRoleNames. The template can only
        // narrow, never expand — a user must already hold a role for the
        // key to use it. When the template was deleted out from under us
        // (race / FK SetNull), the key falls back to the user's full set,
        // mirroring the "no binding" case rather than 401'ing — admins can
        // re-scope after the fact.
        if (matched.RoleTemplateId.HasValue)
        {
            var template = await _db.RoleTemplates.AsNoTracking()
                .FirstOrDefaultAsync(t => t.Id == matched.RoleTemplateId.Value
                                       && t.DeactivatedAt == null);
            if (template is not null)
            {
                List<string>? templateRoles = null;
                try
                {
                    templateRoles = System.Text.Json.JsonSerializer.Deserialize<List<string>>(
                        template.IncludedRoleNamesJson);
                }
                catch
                {
                    // Malformed JSON shouldn't happen (the template-edit
                    // surface controls writes), but if it does, fall back
                    // to the user's full grant set rather than failing the
                    // request — same posture as the malformed-IP-list path.
                    templateRoles = null;
                }
                if (templateRoles is not null)
                {
                    userRoles = userRoles.Intersect(templateRoles, StringComparer.OrdinalIgnoreCase);
                }
            }
        }

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Name, user.GetDisplayName()),
            new("system_api_key_id", matched.Id.ToString()),
            new("system_api_key_prefix", matched.KeyPrefix),
        };

        if (matched.RoleTemplateId.HasValue)
            claims.Add(new("system_api_key_role_template_id",
                matched.RoleTemplateId.Value.ToString()));

        if (!string.IsNullOrEmpty(user.Email))
            claims.Add(new(ClaimTypes.Email, user.Email));

        foreach (var role in userRoles)
            claims.Add(new(ClaimTypes.Role, role));

        var identity = new ClaimsIdentity(claims, SystemApiKeyAuthenticationOptions.SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SystemApiKeyAuthenticationOptions.SchemeName);
        return AuthenticateResult.Success(ticket);
    }

    private static string? ExtractKey(HttpRequest request)
    {
        if (request.Headers.TryGetValue(SystemApiKeyAuthenticationOptions.HeaderName, out var headerValues))
        {
            var value = headerValues.ToString();
            if (!string.IsNullOrWhiteSpace(value))
                return value.Trim();
        }

        if (request.Headers.TryGetValue("Authorization", out var authValues))
        {
            var auth = authValues.ToString();
            if (!string.IsNullOrWhiteSpace(auth)
                && auth.StartsWith(SystemApiKeyAuthenticationOptions.AuthorizationScheme + " ",
                    StringComparison.OrdinalIgnoreCase))
            {
                var key = auth[(SystemApiKeyAuthenticationOptions.AuthorizationScheme.Length + 1)..].Trim();
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
