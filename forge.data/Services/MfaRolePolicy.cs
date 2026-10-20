using System.Text.Json;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Data.Services;

public static class MfaRolePolicy
{
    public const string SettingKey = "mfa.required_roles";
    public const string GraceDaysSettingKey = "mfa.enrollment_grace_days";
    public const int DefaultGraceDays = 7;

    public static async Task<IReadOnlyList<string>?> GetRequiredRolesAsync(AppDbContext db, CancellationToken ct)
    {
        var raw = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == SettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        if (raw is null)
            return null;

        try
        {
            return JsonSerializer.Deserialize<List<string>>(raw) ?? [];
        }
        catch (JsonException)
        {
            return [];
        }
    }

    public static async Task<IReadOnlyList<string>> GetEffectiveRequiredRolesAsync(AppDbContext db, CancellationToken ct)
        => await GetRequiredRolesAsync(db, ct) ?? await LegacyRequiredRolesAsync(db, ct);

    public static async Task<IReadOnlyList<string>> LegacyRequiredRolesAsync(AppDbContext db, CancellationToken ct)
    {
        var holders = await db.UserRoles
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => new { ur.UserId, Role = r.Name! })
            .Join(db.Users, x => x.UserId, u => u.Id, (x, u) => new { x.Role, u.MfaEnforcedByPolicy })
            .ToListAsync(ct);

        return holders
            .GroupBy(h => h.Role, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.All(h => h.MfaEnforcedByPolicy))
            .Select(g => g.Key)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public static async Task SetRequiredRolesAsync(AppDbContext db, IEnumerable<string> roles, CancellationToken ct)
    {
        var normalized = roles
            .Where(r => !string.IsNullOrWhiteSpace(r))
            .Select(r => r.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
            .ToList();
        var value = JsonSerializer.Serialize(normalized);

        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == SettingKey, ct);
        if (existing is null)
            db.SystemSettings.Add(new SystemSetting
            {
                Key = SettingKey,
                Value = value,
                Description = "Roles whose members must enrol in multi-factor authentication",
            });
        else
            existing.Value = value;
    }

    public static async Task<int> GetGraceDaysAsync(AppDbContext db, CancellationToken ct)
    {
        var raw = await db.SystemSettings.AsNoTracking()
            .Where(s => s.Key == GraceDaysSettingKey)
            .Select(s => s.Value)
            .FirstOrDefaultAsync(ct);
        return int.TryParse(raw, out var days) && days >= 0 ? days : DefaultGraceDays;
    }

    public static async Task SetGraceDaysAsync(AppDbContext db, int days, CancellationToken ct)
    {
        var value = days.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == GraceDaysSettingKey, ct);
        if (existing is null)
            db.SystemSettings.Add(new SystemSetting
            {
                Key = GraceDaysSettingKey,
                Value = value,
                Description = "Days a user in an MFA-required role may keep working before enrolling (0 = hold immediately)",
            });
        else
            existing.Value = value;
    }

    public static async Task<bool> IsEnforcedAsync(AppDbContext db, ApplicationUser user, CancellationToken ct)
    {
        var required = await GetRequiredRolesAsync(db, ct);
        if (required is null)
            return user.MfaEnforcedByPolicy;
        if (required.Count == 0)
            return false;

        var userRoles = await RoleNamesAsync(db, user.Id, ct);
        return userRoles.Intersect(required, StringComparer.OrdinalIgnoreCase).Any();
    }

    public static bool AppliesTo(IReadOnlyList<string>? requiredRoles, ApplicationUser user, IEnumerable<string> userRoles)
        => requiredRoles is null
            ? user.MfaEnforcedByPolicy
            : userRoles.Intersect(requiredRoles, StringComparer.OrdinalIgnoreCase).Any();

    private static Task<List<string>> RoleNamesAsync(AppDbContext db, int userId, CancellationToken ct)
        => db.UserRoles
            .Where(ur => ur.UserId == userId)
            .Join(db.Roles, ur => ur.RoleId, r => r.Id, (ur, r) => r.Name!)
            .ToListAsync(ct);
}
