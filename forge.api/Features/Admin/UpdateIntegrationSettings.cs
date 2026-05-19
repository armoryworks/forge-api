using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

using Forge.Core.Models;
using Forge.Core.Settings;
using Forge.Data.Context;

namespace Forge.Api.Features.Admin;

/// <summary>
/// Phase 1m option-3 — persist a single integration's field updates
/// through <see cref="ISettingsService"/>. Replaces the previous
/// per-provider switch that mutated <c>IOptions&lt;...&gt;</c> in-memory
/// against a SystemSetting repository — settings now persist via
/// <see cref="ISettingsService"/>, secrets seal automatically, and the
/// admin UI's editable surface is driven by
/// <see cref="IntegrationDescriptorCatalog"/>.
///
/// Field-key validation: every key in <c>request.Settings</c> must
/// belong to the integration's descriptor — drive-by writes to arbitrary
/// keys are rejected.
///
/// Masked secrets ("••••••••" or all-asterisk legacy form) are skipped —
/// the UI sends the mask back when the user didn't change a sealed
/// value, and we'd corrupt the stored secret if we wrote the mask in.
///
/// IOptions in-memory mutation: restored from the pre-1m handler so
/// admin saves take effect without restart for the 9 integrations whose
/// services bind <c>IOptions&lt;T&gt;</c> (SMTP, MinIO, USPS, DocuSeal,
/// AI, plus the 4 shipping carriers — UPS, FedEx, DHL, Stamps). Migrating
/// those services to <see cref="ISettingsService"/> directly retires
/// this shim — until then the propagation here keeps user-visible
/// behaviour parity with the pre-1m admin handler.
/// </summary>
public record UpdateIntegrationSettingsCommand(
    string Provider,
    Dictionary<string, string> Settings) : IRequest<IntegrationStatusModel>;

public class UpdateIntegrationSettingsHandler(
    ISettingsService settings,
    AppDbContext db,
    IMediator mediator,
    IOptions<SmtpOptions> smtpOptions,
    IOptions<MinioOptions> minioOptions,
    IOptions<UspsOptions> uspsOptions,
    IOptions<DocuSealOptions> docuSealOptions,
    IOptions<AiOptions> aiOptions,
    IOptions<GoogleDriveOptions> googleDriveOptions,
    IOptions<UpsOptions> upsOptions,
    IOptions<FedExOptions> fedExOptions,
    IOptions<DhlOptions> dhlOptions,
    IOptions<StampsOptions> stampsOptions,
    IOptions<QuickBooksOptions> quickBooksOptions,
    IOptions<XeroOptions> xeroOptions,
    IOptions<FreshBooksOptions> freshBooksOptions,
    IOptions<SageOptions> sageOptions,
    IOptions<NetSuiteOptions> netSuiteOptions,
    IOptions<WaveOptions> waveOptions,
    IOptions<ZohoOptions> zohoOptions)
    : IRequestHandler<UpdateIntegrationSettingsCommand, IntegrationStatusModel>
{
    public async Task<IntegrationStatusModel> Handle(UpdateIntegrationSettingsCommand request, CancellationToken ct)
    {
        var integration = IntegrationDescriptorCatalog.FindByProvider(request.Provider)
            ?? throw new KeyNotFoundException(
                $"Unknown integration provider '{request.Provider}'.");

        var allowedKeys = integration.FieldKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Atomic save: wrap every per-field SetAsync in one DB transaction
        // so a validation failure on field N doesn't leave fields 1..N-1
        // half-persisted. Previously, an enum-validator throw mid-loop (e.g.
        // when minio.mode arrived with the wrong case from a free-text input
        // before the dropdown landed) would rollback nothing — earlier
        // fields had already been SaveChanges'd in their own transactions.
        var appliedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        try
        {
            foreach (var (key, value) in request.Settings)
            {
                if (!allowedKeys.Contains(key))
                {
                    throw new InvalidOperationException(
                        $"Setting '{key}' is not part of integration '{request.Provider}'.");
                }

                var descriptor = SettingDescriptorCatalog.FindByKey(key);
                if (descriptor is null) continue;

                if (descriptor.IsSecret && IsMaskedSecret(value)) continue;

                var normalized = string.IsNullOrEmpty(value) ? null : value;
                await settings.SetAsync(key, normalized, ct);
                appliedValues[key] = normalized;
            }
            await tx.CommitAsync(ct);
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }

        // Propagate to IOptions singletons so consuming services pick
        // up the change without a process restart. Only runs after the
        // transaction commits — partial in-memory state on rollback is
        // worse than the old behaviour. Integrations whose services aren't
        // in this list (carriers, accounting providers) continue to require
        // a restart — same behaviour as before phase 1m.
        PropagateToIOptions(request.Provider, appliedValues);

        var current = await mediator.Send(new GetIntegrationSettingsQuery(), ct);
        return current.Integrations.First(i =>
            string.Equals(i.Provider, request.Provider, StringComparison.OrdinalIgnoreCase));
    }

    private void PropagateToIOptions(string provider, Dictionary<string, string?> applied)
    {
        switch (provider.ToLowerInvariant())
        {
            case "smtp":
                ApplySmtp(applied);
                break;
            case "minio":
                ApplyMinio(applied);
                break;
            case "usps":
                ApplyUsps(applied);
                break;
            case "docuseal":
                ApplyDocuSeal(applied);
                break;
            case "ai":
                ApplyAi(applied);
                break;
            case "gdrive":
                ApplyGoogleDrive(applied);
                break;
            case "ups":
                ApplyUps(applied);
                break;
            case "fedex":
                ApplyFedEx(applied);
                break;
            case "dhl":
                ApplyDhl(applied);
                break;
            case "stamps":
                ApplyStamps(applied);
                break;
            case "quickbooks":
                ApplyQuickBooks(applied);
                break;
            case "xero":
                ApplyXero(applied);
                break;
            case "freshbooks":
                ApplyFreshBooks(applied);
                break;
            case "sage":
                ApplySage(applied);
                break;
            case "netsuite":
                ApplyNetSuite(applied);
                break;
            case "wave":
                ApplyWave(applied);
                break;
            case "zoho":
                ApplyZoho(applied);
                break;
        }
    }

    private void ApplySmtp(Dictionary<string, string?> applied)
    {
        var o = smtpOptions.Value;
        if (applied.TryGetValue(SmtpSettings.KeyHost, out var host) && host is not null) o.Host = host;
        if (applied.TryGetValue(SmtpSettings.KeyPort, out var port) && int.TryParse(port, out var p)) o.Port = p;
        if (applied.TryGetValue(SmtpSettings.KeyUsername, out var user)) o.Username = user;
        if (applied.TryGetValue(SmtpSettings.KeyPassword, out var pass) && pass is not null) o.Password = pass;
        if (applied.TryGetValue(SmtpSettings.KeyUseSsl, out var ssl) && bool.TryParse(ssl, out var s)) o.UseSsl = s;
        if (applied.TryGetValue(SmtpSettings.KeyFromAddress, out var from) && from is not null) o.FromAddress = from;
        if (applied.TryGetValue(SmtpSettings.KeyFromName, out var name) && name is not null) o.FromName = name;
    }

    private void ApplyMinio(Dictionary<string, string?> applied)
    {
        var o = minioOptions.Value;
        if (applied.TryGetValue(MinioSettings.KeyEndpoint, out var ep) && ep is not null) o.Endpoint = ep;
        // Public endpoint (browser-facing) — distinct from Internal Endpoint
        // (API-facing). Presigned download URLs are built against this value
        // so end-user browsers can reach MinIO at whatever public hostname
        // / reverse-proxy is configured for the deployment.
        if (applied.TryGetValue(MinioSettings.KeyPublicEndpoint, out var pep) && pep is not null) o.PublicEndpoint = pep;
        if (applied.TryGetValue(MinioSettings.KeyAccessKey, out var ak) && ak is not null) o.AccessKey = ak;
        if (applied.TryGetValue(MinioSettings.KeySecretKey, out var sk) && sk is not null) o.SecretKey = sk;
        if (applied.TryGetValue(MinioSettings.KeyUseSsl, out var ssl) && bool.TryParse(ssl, out var s)) o.UseSsl = s;
        // MinioOptions has role-specific bucket properties (JobFilesBucket,
        // ReceiptsBucket, EmployeeDocsBucket, PiiDocsBucket); the single
        // descriptor "minio.bucket" doesn't map cleanly. Bucket changes
        // remain restart-only until the MinIO descriptor surface is
        // expanded to one entry per role.
    }

    private void ApplyUsps(Dictionary<string, string?> applied)
    {
        var o = uspsOptions.Value;
        // KeyUserId is a legacy alias for KeyConsumerKey (both point at the
        // same DB key today — see UspsSettings.cs). The TryGetValue below
        // therefore covers either name on the wire.
        if (applied.TryGetValue(UspsSettings.KeyConsumerKey, out var ck) && ck is not null)
        {
            o.ConsumerKey = ck;
        }
        // Pre-fix: ConsumerSecret was declared in the descriptor and
        // serialised to the admin UI, but ApplyUsps ignored it on save.
        // The DB row persisted, the running service kept the stale value
        // until the next API restart — a silent gap that looked like
        // "I saved my secret, why doesn't validation work?". Now hot-
        // reloads to match every other secret field on every other
        // descriptor-driven integration.
        if (applied.TryGetValue(UspsSettings.KeyConsumerSecret, out var cs) && cs is not null)
        {
            o.ConsumerSecret = cs;
        }
    }

    private void ApplyDocuSeal(Dictionary<string, string?> applied)
    {
        var o = docuSealOptions.Value;
        if (applied.TryGetValue(DocuSealSettings.KeyApiUrl, out var url) && url is not null) o.BaseUrl = url;
        if (applied.TryGetValue(DocuSealSettings.KeyPublicBaseUrl, out var pub) && pub is not null) o.PublicBaseUrl = pub;
        if (applied.TryGetValue(DocuSealSettings.KeyApiKey, out var key) && key is not null) o.ApiKey = key;
        if (applied.TryGetValue(DocuSealSettings.KeyWebhookSecret, out var ws) && ws is not null) o.WebhookSecret = ws;
        if (applied.TryGetValue(DocuSealSettings.KeyTimeoutSeconds, out var t) && int.TryParse(t, out var ts)) o.TimeoutSeconds = ts;
    }

    private void ApplyGoogleDrive(Dictionary<string, string?> applied)
    {
        var o = googleDriveOptions.Value;
        if (applied.TryGetValue(GoogleDriveSettings.KeyClientId, out var ci) && ci is not null) o.ClientId = ci;
        if (applied.TryGetValue(GoogleDriveSettings.KeyClientSecret, out var cs) && cs is not null) o.ClientSecret = cs;
        if (applied.TryGetValue(GoogleDriveSettings.KeyScopes, out var sc) && sc is not null) o.Scopes = sc;
        // Note: gdrive.mode flips DI registration of ICloudStorageIntegrationService
        // at startup (Mock vs Real). Mode changes require an API restart to swap
        // the registration; the UI surfaces this via the "restart required" toast.
    }

    private void ApplyAi(Dictionary<string, string?> applied)
    {
        var o = aiOptions.Value;
        if (applied.TryGetValue(AiSettings.KeyBaseUrl, out var url) && url is not null) o.BaseUrl = url;
        if (applied.TryGetValue(AiSettings.KeyChatModel, out var m) && m is not null) o.Model = m;
        if (applied.TryGetValue(AiSettings.KeyEmbeddingModel, out var em) && em is not null) o.EmbeddingModel = em;
        if (applied.TryGetValue(AiSettings.KeyVisionModel, out var vm) && vm is not null) o.VisionModel = vm;
        if (applied.TryGetValue(AiSettings.KeyTimeoutSeconds, out var ts) && int.TryParse(ts, out var t)) o.TimeoutSeconds = t;
        if (applied.TryGetValue(AiSettings.KeyVisionTimeoutSeconds, out var vts) && int.TryParse(vts, out var vt)) o.VisionTimeoutSeconds = vt;
        // DocsPath drives IndexDocumentation (the Hangfire RAG-index job).
        // Hot-reload here so an admin can re-point the index at a new
        // mounted directory without restarting the API.
        if (applied.TryGetValue(AiSettings.KeyDocsPath, out var dp) && dp is not null) o.DocsPath = dp;
    }

    // ── Shipping carriers ─────────────────────────────────────────────
    // The four direct carrier services (UPS, FedEx, USPS Shipping, DHL,
    // Stamps.com) all bind IOptions<T>. Without this propagation, an
    // admin save lands the new credentials in the database but the
    // carrier services keep using the in-memory snapshot from process
    // start — a "saved successfully" toast that does nothing until the
    // next API restart. Mirroring the SMTP / MinIO pattern lets carrier
    // credentials take effect on save, same as every other integration.

    private void ApplyUps(Dictionary<string, string?> applied)
    {
        var o = upsOptions.Value;
        if (applied.TryGetValue("ups.client-id", out var cid) && cid is not null) o.ClientId = cid;
        if (applied.TryGetValue("ups.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
        if (applied.TryGetValue("ups.account-number", out var acct) && acct is not null) o.AccountNumber = acct;
        // mode descriptor maps to environment ("sandbox" / "production")
        if (applied.TryGetValue("ups.mode", out var mode) && mode is not null) o.Environment = mode;
    }

    private void ApplyFedEx(Dictionary<string, string?> applied)
    {
        var o = fedExOptions.Value;
        if (applied.TryGetValue("fedex.client-id", out var cid) && cid is not null) o.ClientId = cid;
        if (applied.TryGetValue("fedex.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
        if (applied.TryGetValue("fedex.account-number", out var acct) && acct is not null) o.AccountNumber = acct;
        if (applied.TryGetValue("fedex.mode", out var mode) && mode is not null) o.Environment = mode;
    }

    private void ApplyDhl(Dictionary<string, string?> applied)
    {
        var o = dhlOptions.Value;
        if (applied.TryGetValue("dhl.api-key", out var key) && key is not null) o.ApiKey = key;
        if (applied.TryGetValue("dhl.account-number", out var acct) && acct is not null) o.AccountNumber = acct;
        // dhl.mode is in the descriptor but DhlOptions doesn't model an
        // environment switch — the BaseUrl is hardcoded to production.
        // Sandbox vs production for DHL Express is gated server-side by
        // the API key tier the developer was issued. No-op here.
    }

    private void ApplyStamps(Dictionary<string, string?> applied)
    {
        var o = stampsOptions.Value;
        // Stamps descriptor uses username/password/integration-id; the
        // options model has ApiKey + AccountId + Password + Environment.
        // Map integration-id → ApiKey, username → AccountId, password →
        // Password (closest available fields). Until a real Stamps
        // service ships (the SwsimV111 SOAP wrapper), this captures the
        // credentials without a restart so when the service does land
        // it picks them up immediately — and the password is no longer
        // silently dropped on the floor.
        if (applied.TryGetValue("stamps.integration-id", out var iid) && iid is not null) o.ApiKey = iid;
        if (applied.TryGetValue("stamps.username", out var user) && user is not null) o.AccountId = user;
        if (applied.TryGetValue("stamps.password", out var pw) && pw is not null) o.Password = pw;
        if (applied.TryGetValue("stamps.mode", out var mode) && mode is not null) o.Environment = mode;
    }

    // ── Accounting providers ──────────────────────────────────────────
    // All 7 accounting providers bind IOptions<T>; admin saves through
    // /admin/integrations land in system_settings, and the Apply* methods
    // below hot-reload the running singleton so service calls (incl. the
    // existing AccountingController OAuth /authorize endpoints) pick up
    // the new credentials without a process restart.
    //
    // Realm / tenant / account / organisation IDs that come back from
    // OAuth callbacks (e.g. QuickBooks RealmId) are NOT applied here —
    // they're persisted by the OAuth-completion path on the controller
    // (or the future unified OAuth handler), not by an admin save.

    private void ApplyQuickBooks(Dictionary<string, string?> applied)
    {
        var o = quickBooksOptions.Value;
        if (applied.TryGetValue("quickbooks.client-id", out var ci) && ci is not null) o.ClientId = ci;
        if (applied.TryGetValue("quickbooks.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
        if (applied.TryGetValue("quickbooks.mode", out var mode) && mode is not null)
        {
            // QuickBooksOptions.Environment is "sandbox" / "production" —
            // map Mock-Real-Disabled by Real => production, anything else => sandbox.
            o.Environment = string.Equals(mode, IntegrationModeChoices.Real, StringComparison.OrdinalIgnoreCase)
                ? "production"
                : "sandbox";
        }
    }

    private void ApplyXero(Dictionary<string, string?> applied)
    {
        var o = xeroOptions.Value;
        if (applied.TryGetValue("xero.client-id", out var ci) && ci is not null) o.ClientId = ci;
        if (applied.TryGetValue("xero.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
    }

    private void ApplyFreshBooks(Dictionary<string, string?> applied)
    {
        var o = freshBooksOptions.Value;
        if (applied.TryGetValue("freshbooks.client-id", out var ci) && ci is not null) o.ClientId = ci;
        if (applied.TryGetValue("freshbooks.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
    }

    private void ApplySage(Dictionary<string, string?> applied)
    {
        var o = sageOptions.Value;
        if (applied.TryGetValue("sage.client-id", out var ci) && ci is not null) o.ClientId = ci;
        if (applied.TryGetValue("sage.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
        if (applied.TryGetValue("sage.country-code", out var cc) && cc is not null) o.CountryCode = cc;
    }

    private void ApplyNetSuite(Dictionary<string, string?> applied)
    {
        // NetSuite uses Token-Based Authentication — no client_id/secret
        // OAuth flow. All 5 credentials are admin-entered + persisted.
        var o = netSuiteOptions.Value;
        if (applied.TryGetValue("netsuite.account-id", out var ai) && ai is not null) o.AccountId = ai;
        if (applied.TryGetValue("netsuite.consumer-key", out var ck) && ck is not null) o.ConsumerKey = ck;
        if (applied.TryGetValue("netsuite.consumer-secret", out var cs) && cs is not null) o.ConsumerSecret = cs;
        if (applied.TryGetValue("netsuite.token-id", out var ti) && ti is not null) o.TokenId = ti;
        if (applied.TryGetValue("netsuite.token-secret", out var ts) && ts is not null) o.TokenSecret = ts;
    }

    private void ApplyWave(Dictionary<string, string?> applied)
    {
        // Wave uses a personal access token (or OAuth-issued bearer) —
        // admin pastes it directly. No client-id/secret flow. Pre-fix
        // descriptor wrongly used ProviderBlock (client-id/secret keys);
        // those keys are now removed from AccountingSettings and replaced
        // with wave.access-token + wave.business-id.
        var o = waveOptions.Value;
        if (applied.TryGetValue("wave.access-token", out var at) && at is not null) o.AccessToken = at;
        if (applied.TryGetValue("wave.business-id", out var bi) && bi is not null) o.BusinessId = bi;
    }

    private void ApplyZoho(Dictionary<string, string?> applied)
    {
        var o = zohoOptions.Value;
        if (applied.TryGetValue("zoho.client-id", out var ci) && ci is not null) o.ClientId = ci;
        if (applied.TryGetValue("zoho.client-secret", out var cs) && cs is not null) o.ClientSecret = cs;
        if (applied.TryGetValue("zoho.organization-id", out var oi) && oi is not null) o.OrganizationId = oi;
        if (applied.TryGetValue("zoho.data-center", out var dc) && dc is not null) o.DataCenter = dc;
    }

    private static bool IsMaskedSecret(string value)
        => !string.IsNullOrEmpty(value) && value.All(c => c == '•' || c == '*');
}
