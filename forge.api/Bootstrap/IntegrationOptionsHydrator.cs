using Microsoft.Extensions.Options;

using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Api.Bootstrap;

/// <summary>
/// One-shot startup task that pulls each integration's admin-managed
/// settings out of <c>system_settings</c> (via <see cref="ISettingsService"/>)
/// and overlays them onto the live <see cref="IOptions{T}"/> singletons.
///
/// Why this exists: the per-integration <c>Apply*</c> hot-reload in
/// <c>UpdateIntegrationSettingsHandler</c> takes effect on the running
/// process when an admin saves through the UI — but ON RESTART, the
/// <see cref="IOptions{T}"/> singletons reset to whatever the
/// <c>appsettings.json</c> binding produced (typically empty strings,
/// because we're moving away from config-file-based integration setup).
/// Without this hydrator, every restart would wipe the admin-configured
/// values and force operators to either re-save through the UI or pin
/// them in <c>appsettings.json</c>.
///
/// Runs after migrations + the main seed (so the system_settings table
/// definitely exists). Secrets auto-unseal via Data Protection during
/// the <see cref="ISettingsService"/> read, so the live <c>IOptions</c>
/// gets the cleartext value the service needs.
///
/// Adding a new integration: extend <see cref="HydrateAsync"/> with one
/// more provider block. Mirror the shape of the existing Drive entry.
/// </summary>
public class IntegrationOptionsHydrator(
    ISettingsService settings,
    IOptions<GoogleDriveOptions> googleDriveOptions,
    ILogger<IntegrationOptionsHydrator> logger)
{
    public async Task HydrateAsync(CancellationToken ct = default)
    {
        var providersHydrated = 0;

        // ── Google Drive ───────────────────────────────────────────────
        var driveCi = await settings.GetStringAsync(GoogleDriveSettings.KeyClientId, ct);
        var driveCs = await settings.GetStringAsync(GoogleDriveSettings.KeyClientSecret, ct);
        var driveSc = await settings.GetStringAsync(GoogleDriveSettings.KeyScopes, ct);
        var driveTouched = false;
        if (!string.IsNullOrEmpty(driveCi)) { googleDriveOptions.Value.ClientId = driveCi; driveTouched = true; }
        if (!string.IsNullOrEmpty(driveCs)) { googleDriveOptions.Value.ClientSecret = driveCs; driveTouched = true; }
        if (!string.IsNullOrEmpty(driveSc)) { googleDriveOptions.Value.Scopes = driveSc; driveTouched = true; }
        if (driveTouched) providersHydrated++;

        logger.LogInformation(
            "[INTEGRATION-HYDRATE] Hydrated {Count} integration options from system_settings",
            providersHydrated);
    }
}
