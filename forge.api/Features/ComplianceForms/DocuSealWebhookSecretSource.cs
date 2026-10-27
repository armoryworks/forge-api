using Microsoft.Extensions.Options;

using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Api.Features.ComplianceForms;

public sealed class DocuSealWebhookSecretSource(ISettingsService settings, IOptions<DocuSealOptions> options)
{
    public async Task<string?> GetAsync(CancellationToken ct)
    {
        var saved = await settings.GetStringAsync(DocuSealSettings.KeyWebhookSecret, ct);
        if (!string.IsNullOrWhiteSpace(saved)) return saved;

        var configured = options.Value.WebhookSecret;
        return string.IsNullOrWhiteSpace(configured) ? null : configured;
    }
}
