namespace Forge.Core.Settings;

/// <summary>
/// Phase 1m — typed reader / writer over <c>SystemSetting</c>. Per-request
/// scoped (one cache per HTTP request); pulls all rows on first access
/// to avoid N+1 when handlers read many settings (e.g. building an
/// <c>OAuthImapOptions</c> hydrate hits 5 keys).
///
/// Reads return the descriptor's <c>DefaultValue</c> when no DB row is
/// present, so handlers can call <see cref="GetString"/> without nil-
/// checking. Secrets unseal automatically before return.
///
/// Write path goes through <see cref="SetAsync"/>: validates against the
/// descriptor (data-type parsing, regex pattern), encrypts secrets, then
/// upserts via the existing <c>ISystemSettingRepository</c>.
/// </summary>
public interface ISettingsService
{
    /// <summary>Get the raw string value for a key. Returns the
    /// descriptor's DefaultValue when the DB has no row. Secret values
    /// auto-unseal.</summary>
    Task<string?> GetStringAsync(string key, CancellationToken ct = default);

    Task<bool> GetBoolAsync(string key, CancellationToken ct = default);
    Task<int> GetIntAsync(string key, CancellationToken ct = default);

    /// <summary>Persist a value. Re-encrypts secrets on write. Validates
    /// data-type + regex pattern from the descriptor; throws
    /// <see cref="InvalidOperationException"/> on invalid input or
    /// unknown key.</summary>
    Task SetAsync(string key, string? value, CancellationToken ct = default);

    /// <summary>Bulk read for one group — used by the admin UI to render
    /// the editor + by typed-options hydration helpers. Returned
    /// dictionary is keyed by setting key; values are unsealed already.</summary>
    Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default);
}
