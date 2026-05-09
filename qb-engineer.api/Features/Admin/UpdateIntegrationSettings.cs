using MediatR;
using Microsoft.Extensions.Options;

using QBEngineer.Core.Models;
using QBEngineer.Core.Settings;

namespace QBEngineer.Api.Features.Admin;

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
/// admin saves take effect without restart for the 5 integrations whose
/// services bind <c>IOptions&lt;T&gt;</c> (SMTP, MinIO, USPS, DocuSeal,
/// AI). Migrating those services to <see cref="ISettingsService"/>
/// directly retires this shim — until then the propagation here keeps
/// user-visible behaviour parity with the pre-1m admin handler.
/// </summary>
public record UpdateIntegrationSettingsCommand(
    string Provider,
    Dictionary<string, string> Settings) : IRequest<IntegrationStatusModel>;

public class UpdateIntegrationSettingsHandler(
    ISettingsService settings,
    IMediator mediator,
    IOptions<SmtpOptions> smtpOptions,
    IOptions<MinioOptions> minioOptions,
    IOptions<UspsOptions> uspsOptions,
    IOptions<DocuSealOptions> docuSealOptions,
    IOptions<AiOptions> aiOptions)
    : IRequestHandler<UpdateIntegrationSettingsCommand, IntegrationStatusModel>
{
    public async Task<IntegrationStatusModel> Handle(UpdateIntegrationSettingsCommand request, CancellationToken ct)
    {
        var integration = IntegrationDescriptorCatalog.FindByProvider(request.Provider)
            ?? throw new KeyNotFoundException(
                $"Unknown integration provider '{request.Provider}'.");

        var allowedKeys = integration.FieldKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

        var appliedValues = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
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

        // Propagate to IOptions singletons so consuming services pick
        // up the change without a process restart. Integrations whose
        // services aren't in this list (carriers, accounting providers)
        // continue to require a restart — same behaviour as before
        // phase 1m.
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
        // UspsOptions has ConsumerKey + ConsumerSecret in legacy code, but the
        // descriptor only declares a single user-id field today. Mirror the
        // value into ConsumerKey for service-side compatibility until the
        // USPS service migrates off IOptions.
        if (applied.TryGetValue(UspsSettings.KeyUserId, out var uid) && uid is not null)
        {
            o.ConsumerKey = uid;
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

    private void ApplyAi(Dictionary<string, string?> applied)
    {
        var o = aiOptions.Value;
        if (applied.TryGetValue(AiSettings.KeyBaseUrl, out var url) && url is not null) o.BaseUrl = url;
        if (applied.TryGetValue(AiSettings.KeyChatModel, out var m) && m is not null) o.Model = m;
        if (applied.TryGetValue(AiSettings.KeyEmbeddingModel, out var em) && em is not null) o.EmbeddingModel = em;
        if (applied.TryGetValue(AiSettings.KeyVisionModel, out var vm) && vm is not null) o.VisionModel = vm;
        if (applied.TryGetValue(AiSettings.KeyTimeoutSeconds, out var ts) && int.TryParse(ts, out var t)) o.TimeoutSeconds = t;
        if (applied.TryGetValue(AiSettings.KeyVisionTimeoutSeconds, out var vts) && int.TryParse(vts, out var vt)) o.VisionTimeoutSeconds = vt;
    }

    private static bool IsMaskedSecret(string value)
        => !string.IsNullOrEmpty(value) && value.All(c => c == '•' || c == '*');
}
