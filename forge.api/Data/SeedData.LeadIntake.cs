using System.Security.Cryptography;

using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;
using Serilog;

namespace Forge.Api.Data;

/// <summary>
/// First-boot bootstrap for the headless lead-intake service identity:
///
///   - <c>ApplicationUser</c> with email <c>lead-intake-system@forge.local</c>,
///     password disabled, in the <c>LeadIntake</c> role.
///   - One <see cref="SystemApiKey"/> for that user with a securely-generated
///     plaintext value (32 bytes, base64url).
///
/// Idempotent — the user is created only if it doesn't already exist, and a
/// key is issued only if the user has zero existing keys (active OR revoked).
/// This means the bootstrap key is generated EXACTLY ONCE in the lifetime
/// of the install; subsequent boots are no-ops. To rotate, an admin issues
/// a new key via <c>POST /api/v1/admin/system-api-keys</c> and revokes the
/// old one via <c>DELETE /api/v1/admin/system-api-keys/{id}</c>.
///
/// The plaintext is logged ONCE at <c>[API-KEY-BOOTSTRAP]</c> level so the
/// operator can copy it into their integration's secrets store. After that
/// boot, the plaintext is gone — only the PBKDF2 hash remains. Operators
/// who miss the log line MUST rotate via the admin endpoint.
///
/// See <c>docs/api-key-integrations.md</c> for the full contract.
/// </summary>
public static partial class SeedData
{
    private const string LeadIntakeUserEmail = "lead-intake-system@forge.local";

    /// <summary>
    /// Role granted to headless service identities (e.g. the lead-intake
    /// integration principal). Exposed so consumers like the employee roster
    /// can exclude these non-human accounts from people-facing lists without
    /// hard-coding the literal.
    /// </summary>
    public const string LeadIntakeRoleName = "LeadIntake";

    public static async Task SeedLeadIntakeBootstrapAsync(IServiceProvider services)
    {
        var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();
        var db = services.GetRequiredService<AppDbContext>();

        // 1. Ensure the service user exists.
        var user = await userManager.FindByEmailAsync(LeadIntakeUserEmail);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = LeadIntakeUserEmail,
                Email = LeadIntakeUserEmail,
                FirstName = "Lead Intake",
                LastName = "Service",
                Initials = "LI",
                AvatarColor = "#64748b",
                EmailConfirmed = true,
                IsActive = true,
            };

            // Use CreateAsync(user) with no password. UserManager creates the
            // row, then SetPasswordHashAsync(null) explicitly nulls the hash
            // so password-based login paths (Login.cs) cannot authenticate.
            // SSO and API key paths remain open per their own credentials.
            var createResult = await userManager.CreateAsync(user);
            if (!createResult.Succeeded)
            {
                Log.Error(
                    "[API-KEY-BOOTSTRAP] Failed to create lead-intake service user {Email}: {Errors}",
                    LeadIntakeUserEmail,
                    string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return;
            }

            await userManager.AddToRoleAsync(user, LeadIntakeRoleName);
            await userManager.SetLockoutEnabledAsync(user, false);
            await userManager.RemovePasswordAsync(user);  // ensures hash is null

            Log.Information(
                "[API-KEY-BOOTSTRAP] Created service user {Email} in role {Role}",
                LeadIntakeUserEmail, LeadIntakeRoleName);
        }
        else if (!(await userManager.GetRolesAsync(user)).Contains(LeadIntakeRoleName))
        {
            // Self-heal: user exists but lost role assignment (manual edit,
            // bad migration, etc.). Re-add the role rather than erroring.
            await userManager.AddToRoleAsync(user, LeadIntakeRoleName);
            Log.Information(
                "[API-KEY-BOOTSTRAP] Re-added {Role} grant on existing service user {Email}",
                LeadIntakeRoleName, LeadIntakeUserEmail);
        }

        // 2. Issue one key IF this user has zero keys ever. We deliberately
        // check active+revoked together so re-running the seeder doesn't
        // proliferate keys after an admin has rotated and revoked.
        var hasAnyKey = await db.SystemApiKeys
            .AnyAsync(k => k.UserId == user.Id);
        if (hasAnyKey)
        {
            Log.Debug(
                "[API-KEY-BOOTSTRAP] Service user {Email} already has at least one " +
                "SystemApiKey on file; skipping bootstrap key issuance.",
                LeadIntakeUserEmail);
            return;
        }

        // Generate 32 random bytes, base64url-encode, prefix with `fsk_`.
        // Mirrors the issuance shape in CreateSystemApiKeyHandler so the
        // bootstrap key behaves identically downstream.
        var keyBytes = RandomNumberGenerator.GetBytes(32);
        var plaintextKey = $"fsk_{Convert.ToBase64String(keyBytes)
            .Replace("+", "").Replace("/", "").Replace("=", "")}";
        var keyPrefix = plaintextKey[..12];

        var hasher = new PasswordHasher<object>();
        var keyHash = hasher.HashPassword(null!, plaintextKey);

        var apiKey = new SystemApiKey
        {
            Name = "Lead intake bootstrap key (first boot)",
            KeyHash = keyHash,
            KeyPrefix = keyPrefix,
            UserId = user.Id,
            IsActive = true,
            // Deliberately no ExpiresAt — operators control rotation. The
            // bootstrap key has no built-in TTL; admins can revoke + reissue
            // via the admin endpoint whenever required.
        };

        db.SystemApiKeys.Add(apiKey);
        await db.SaveChangesAsync();

        // ── THE plaintext log line ───────────────────────────────────────
        // Printed ONCE. Never persisted. Operators MUST capture this on
        // first boot or rotate via the admin endpoint. The `[API-KEY-
        // BOOTSTRAP]` prefix is the searchable handle.
        Log.Warning(
            "[API-KEY-BOOTSTRAP] One-time lead-intake key issued for {Email} (prefix {Prefix}). " +
            "Copy this plaintext to your integration's secrets store NOW — it will not be " +
            "shown again. To rotate, issue a new key via POST /api/v1/admin/system-api-keys " +
            "and revoke the old one via DELETE /api/v1/admin/system-api-keys/{{id}}.\n" +
            "    PLAINTEXT KEY: {PlaintextKey}",
            LeadIntakeUserEmail, keyPrefix, plaintextKey);
    }
}
