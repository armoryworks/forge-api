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
    IOptions<QuickBooksOptions> quickBooksOptions,
    IOptions<XeroOptions> xeroOptions,
    IOptions<FreshBooksOptions> freshBooksOptions,
    IOptions<SageOptions> sageOptions,
    IOptions<NetSuiteOptions> netSuiteOptions,
    IOptions<WaveOptions> waveOptions,
    IOptions<ZohoOptions> zohoOptions,
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

        // ── QuickBooks Online ──────────────────────────────────────────
        var qbCi = await settings.GetStringAsync("quickbooks.client-id", ct);
        var qbCs = await settings.GetStringAsync("quickbooks.client-secret", ct);
        var qbMode = await settings.GetStringAsync("quickbooks.mode", ct);
        var qbTouched = false;
        if (!string.IsNullOrEmpty(qbCi)) { quickBooksOptions.Value.ClientId = qbCi; qbTouched = true; }
        if (!string.IsNullOrEmpty(qbCs)) { quickBooksOptions.Value.ClientSecret = qbCs; qbTouched = true; }
        if (!string.IsNullOrEmpty(qbMode))
        {
            // Map IntegrationModeChoices to QuickBooksOptions.Environment.
            quickBooksOptions.Value.Environment = string.Equals(qbMode, "Real", StringComparison.OrdinalIgnoreCase)
                ? "production" : "sandbox";
            qbTouched = true;
        }
        if (qbTouched) providersHydrated++;

        // ── Xero ───────────────────────────────────────────────────────
        var xeroCi = await settings.GetStringAsync("xero.client-id", ct);
        var xeroCs = await settings.GetStringAsync("xero.client-secret", ct);
        var xeroTouched = false;
        if (!string.IsNullOrEmpty(xeroCi)) { xeroOptions.Value.ClientId = xeroCi; xeroTouched = true; }
        if (!string.IsNullOrEmpty(xeroCs)) { xeroOptions.Value.ClientSecret = xeroCs; xeroTouched = true; }
        if (xeroTouched) providersHydrated++;

        // ── FreshBooks ─────────────────────────────────────────────────
        var fbCi = await settings.GetStringAsync("freshbooks.client-id", ct);
        var fbCs = await settings.GetStringAsync("freshbooks.client-secret", ct);
        var fbTouched = false;
        if (!string.IsNullOrEmpty(fbCi)) { freshBooksOptions.Value.ClientId = fbCi; fbTouched = true; }
        if (!string.IsNullOrEmpty(fbCs)) { freshBooksOptions.Value.ClientSecret = fbCs; fbTouched = true; }
        if (fbTouched) providersHydrated++;

        // ── Sage ───────────────────────────────────────────────────────
        var sageCi = await settings.GetStringAsync("sage.client-id", ct);
        var sageCs = await settings.GetStringAsync("sage.client-secret", ct);
        var sageCc = await settings.GetStringAsync("sage.country-code", ct);
        var sageTouched = false;
        if (!string.IsNullOrEmpty(sageCi)) { sageOptions.Value.ClientId = sageCi; sageTouched = true; }
        if (!string.IsNullOrEmpty(sageCs)) { sageOptions.Value.ClientSecret = sageCs; sageTouched = true; }
        if (!string.IsNullOrEmpty(sageCc)) { sageOptions.Value.CountryCode = sageCc; sageTouched = true; }
        if (sageTouched) providersHydrated++;

        // ── NetSuite (Token-Based Auth) ────────────────────────────────
        var nsAi = await settings.GetStringAsync("netsuite.account-id", ct);
        var nsCk = await settings.GetStringAsync("netsuite.consumer-key", ct);
        var nsCs = await settings.GetStringAsync("netsuite.consumer-secret", ct);
        var nsTi = await settings.GetStringAsync("netsuite.token-id", ct);
        var nsTs = await settings.GetStringAsync("netsuite.token-secret", ct);
        var nsTouched = false;
        if (!string.IsNullOrEmpty(nsAi)) { netSuiteOptions.Value.AccountId = nsAi; nsTouched = true; }
        if (!string.IsNullOrEmpty(nsCk)) { netSuiteOptions.Value.ConsumerKey = nsCk; nsTouched = true; }
        if (!string.IsNullOrEmpty(nsCs)) { netSuiteOptions.Value.ConsumerSecret = nsCs; nsTouched = true; }
        if (!string.IsNullOrEmpty(nsTi)) { netSuiteOptions.Value.TokenId = nsTi; nsTouched = true; }
        if (!string.IsNullOrEmpty(nsTs)) { netSuiteOptions.Value.TokenSecret = nsTs; nsTouched = true; }
        if (nsTouched) providersHydrated++;

        // ── Wave (personal access token) ───────────────────────────────
        var waveAt = await settings.GetStringAsync("wave.access-token", ct);
        var waveBi = await settings.GetStringAsync("wave.business-id", ct);
        var waveTouched = false;
        if (!string.IsNullOrEmpty(waveAt)) { waveOptions.Value.AccessToken = waveAt; waveTouched = true; }
        if (!string.IsNullOrEmpty(waveBi)) { waveOptions.Value.BusinessId = waveBi; waveTouched = true; }
        if (waveTouched) providersHydrated++;

        // ── Zoho Books ─────────────────────────────────────────────────
        var zohoCi = await settings.GetStringAsync("zoho.client-id", ct);
        var zohoCs = await settings.GetStringAsync("zoho.client-secret", ct);
        var zohoOi = await settings.GetStringAsync("zoho.organization-id", ct);
        var zohoDc = await settings.GetStringAsync("zoho.data-center", ct);
        var zohoTouched = false;
        if (!string.IsNullOrEmpty(zohoCi)) { zohoOptions.Value.ClientId = zohoCi; zohoTouched = true; }
        if (!string.IsNullOrEmpty(zohoCs)) { zohoOptions.Value.ClientSecret = zohoCs; zohoTouched = true; }
        if (!string.IsNullOrEmpty(zohoOi)) { zohoOptions.Value.OrganizationId = zohoOi; zohoTouched = true; }
        if (!string.IsNullOrEmpty(zohoDc)) { zohoOptions.Value.DataCenter = zohoDc; zohoTouched = true; }
        if (zohoTouched) providersHydrated++;

        logger.LogInformation(
            "[INTEGRATION-HYDRATE] Hydrated {Count} integration options from system_settings",
            providersHydrated);
    }
}
