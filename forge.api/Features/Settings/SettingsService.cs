using System.Globalization;
using System.Text.RegularExpressions;

using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Settings;

/// <summary>
/// Phase 1m — production <see cref="ISettingsService"/> backed by the
/// <c>system_settings</c> table + <c>IDataProtectionProvider</c> for
/// secret seal/unseal.
///
/// Caching: one in-memory snapshot per service instance. Registered
/// scoped (one per HTTP request) so cross-request changes propagate
/// naturally on the next request without a manual invalidation hook.
/// Within a single request, the cache prevents repeated SELECTs when
/// a handler hydrates several typed config classes.
/// </summary>
public class SettingsService(
    AppDbContext db,
    IDataProtectionProvider dataProtection) : ISettingsService
{
    private const string ProtectorPurpose = "settings.secret";
    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);

    // Cache covers the full key universe so reads after the first one
    // are O(1) lookups without hitting the DB. Values stored as-stored
    // (sealed for secrets); unsealing happens at read time so writes
    // don't have to invalidate-then-rewrite the cache.
    private Dictionary<string, string>? _cache;

    public async Task<string?> GetStringAsync(string key, CancellationToken ct = default)
    {
        var descriptor = SettingDescriptorCatalog.FindByKey(key);
        if (descriptor is null)
        {
            throw new InvalidOperationException(
                $"Setting key '{key}' is not registered. Add a descriptor in the appropriate "
                + "Settings static class.");
        }

        var raw = await GetRawAsync(key, ct);
        if (raw is null) return descriptor.DefaultValue;

        if (!descriptor.IsSecret) return raw;

        // Secrets are sealed in the DB. If unseal fails (key rotation
        // without re-entry, manual DB tampering), treat the value as
        // unset so the caller falls back to the default + the admin
        // can re-enter.
        try { return _protector.Unprotect(raw); }
        catch { return descriptor.DefaultValue; }
    }

    public async Task<bool> GetBoolAsync(string key, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        return string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<int> GetIntAsync(string key, CancellationToken ct = default)
    {
        var raw = await GetStringAsync(key, ct);
        return int.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out var v) ? v : 0;
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var descriptor = SettingDescriptorCatalog.FindByKey(key)
            ?? throw new InvalidOperationException(
                $"Setting key '{key}' is not registered.");

        ValidateValueForDescriptor(descriptor, value);

        // Persisted form: secret-sealed-or-plaintext; null/empty erases
        // the row so future reads return the default rather than an
        // empty string.
        if (string.IsNullOrEmpty(value))
        {
            var existing = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
            if (existing is not null)
            {
                db.SystemSettings.Remove(existing);
                await db.SaveChangesAsync(ct);
                _cache?.Remove(key);
            }
            return;
        }

        var stored = descriptor.IsSecret ? _protector.Protect(value) : value;

        var row = await db.SystemSettings.FirstOrDefaultAsync(s => s.Key == key, ct);
        if (row is null)
        {
            db.SystemSettings.Add(new SystemSetting
            {
                Key = key,
                Value = stored,
                Description = descriptor.Description,
            });
        }
        else
        {
            row.Value = stored;
            row.Description = descriptor.Description;
        }
        await db.SaveChangesAsync(ct);

        if (_cache is not null) _cache[key] = stored;
    }

    public async Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(
        string group, CancellationToken ct = default)
    {
        var descriptors = SettingDescriptorCatalog.ForGroup(group);
        var result = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var d in descriptors)
        {
            result[d.Key] = await GetStringAsync(d.Key, ct);
        }
        return result;
    }

    private async Task<string?> GetRawAsync(string key, CancellationToken ct)
    {
        if (_cache is null) await PopulateCacheAsync(ct);
        return _cache!.TryGetValue(key, out var v) ? v : null;
    }

    private async Task PopulateCacheAsync(CancellationToken ct)
    {
        var rows = await db.SystemSettings.AsNoTracking()
            .Select(s => new { s.Key, s.Value })
            .ToListAsync(ct);
        _cache = rows.ToDictionary(r => r.Key, r => r.Value, StringComparer.OrdinalIgnoreCase);
    }

    private static void ValidateValueForDescriptor(SettingDescriptor descriptor, string? value)
    {
        if (string.IsNullOrEmpty(value)) return; // empty erases; not validated

        switch (descriptor.DataType)
        {
            case SettingDataType.Boolean:
                if (!bool.TryParse(value, out _))
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' must be true or false.");
                break;

            case SettingDataType.Integer:
                if (!int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' must be a whole number.");
                break;

            case SettingDataType.Url:
                if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' must be an absolute URL.");
                break;

            case SettingDataType.Enum:
                if (descriptor.Choices is { Count: > 0 } choices
                    && !choices.Any(c => string.Equals(c.Value, value, StringComparison.Ordinal)))
                    throw new InvalidOperationException(
                        $"'{descriptor.DisplayName}' must be one of: "
                        + string.Join(", ", choices.Select(c => c.Value)));
                break;
        }

        if (!string.IsNullOrEmpty(descriptor.ValidationPattern)
            && !Regex.IsMatch(value, descriptor.ValidationPattern))
        {
            throw new InvalidOperationException(
                $"'{descriptor.DisplayName}' does not match the required pattern.");
        }
    }
}
