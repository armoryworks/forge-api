using MediatR;

using QBEngineer.Core.Models;
using QBEngineer.Core.Settings;

namespace QBEngineer.Api.Features.Admin;

/// <summary>
/// Phase 1m option-3 — persist a single integration's field updates
/// through <see cref="ISettingsService"/>. Replaces the previous
/// per-provider switch that mutated <c>IOptions&lt;...&gt;</c> in-memory;
/// services now read live values directly from the settings service so
/// no in-memory propagation is needed.
///
/// Field-key validation: every key in <c>request.Settings</c> must
/// belong to the integration's descriptor (per
/// <see cref="IntegrationDescriptorCatalog"/>) — drive-by writes to
/// arbitrary keys via this endpoint are rejected.
///
/// Masked secrets ("••••••••" or all-asterisk legacy form) are skipped —
/// the UI sends the mask back when the user didn't change a sealed
/// value, and we'd corrupt the stored secret if we wrote the mask in.
/// </summary>
public record UpdateIntegrationSettingsCommand(
    string Provider,
    Dictionary<string, string> Settings) : IRequest<IntegrationStatusModel>;

public class UpdateIntegrationSettingsHandler(
    ISettingsService settings,
    GetIntegrationSettingsHandler getHandler)
    : IRequestHandler<UpdateIntegrationSettingsCommand, IntegrationStatusModel>
{
    public async Task<IntegrationStatusModel> Handle(UpdateIntegrationSettingsCommand request, CancellationToken ct)
    {
        var integration = IntegrationDescriptorCatalog.FindByProvider(request.Provider)
            ?? throw new KeyNotFoundException(
                $"Unknown integration provider '{request.Provider}'.");

        var allowedKeys = integration.FieldKeys.ToHashSet(StringComparer.OrdinalIgnoreCase);

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

            await settings.SetAsync(key, string.IsNullOrEmpty(value) ? null : value, ct);
        }

        var current = await getHandler.Handle(new GetIntegrationSettingsQuery(), ct);
        return current.Integrations.First(i =>
            string.Equals(i.Provider, request.Provider, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsMaskedSecret(string value)
        => !string.IsNullOrEmpty(value) && value.All(c => c == '•' || c == '*');
}
