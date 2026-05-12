using MediatR;

using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Api.Features.Admin;

/// <summary>
/// Phase 1m option-3 — projects every <see cref="IntegrationDescriptor"/>
/// into the <c>IntegrationStatusModel</c> shape the existing admin
/// integrations page consumes. Replaces the previously-inline
/// per-integration metadata with a catalog-driven projection.
///
/// Field values come from <see cref="ISettingsService"/> — secrets are
/// returned masked, non-secrets are returned plaintext, unset fields
/// fall back to the descriptor's DefaultValue.
/// </summary>
public record GetIntegrationSettingsQuery : IRequest<IntegrationSettingsResult>;

public class GetIntegrationSettingsHandler(ISettingsService settings)
    : IRequestHandler<GetIntegrationSettingsQuery, IntegrationSettingsResult>
{
    private const string SecretMask = "••••••••";

    public async Task<IntegrationSettingsResult> Handle(GetIntegrationSettingsQuery request, CancellationToken ct)
    {
        var integrations = new List<IntegrationStatusModel>(IntegrationDescriptorCatalog.All.Count);

        foreach (var integration in IntegrationDescriptorCatalog.All)
        {
            var fields = new List<IntegrationSettingField>(integration.FieldKeys.Count);
            string? primaryValue = null;

            foreach (var key in integration.FieldKeys)
            {
                var descriptor = SettingDescriptorCatalog.FindByKey(key);
                if (descriptor is null) continue; // safety — schema typo

                var raw = await settings.GetStringAsync(key, ct);
                var hasValue = !string.IsNullOrEmpty(raw) && raw != descriptor.DefaultValue;
                var display = descriptor.IsSecret && hasValue ? SecretMask : raw ?? string.Empty;

                if (key == integration.IsConfiguredCheckKey) primaryValue = raw;

                fields.Add(new IntegrationSettingField(
                    Key: descriptor.Key,
                    Label: descriptor.DisplayName,
                    Value: display,
                    IsSensitive: descriptor.IsSecret,
                    IsRequired: descriptor.IsRequired,
                    InputType: MapInputType(descriptor)));
            }

            // Default IsConfigured rule: the IsConfiguredCheckKey has a
            // non-empty stored value. Falls back to "any required field
            // populated" when the descriptor doesn't pin a key.
            var isConfigured = integration.IsConfiguredCheckKey is not null
                ? !string.IsNullOrEmpty(primaryValue)
                : fields.Any(f => f.IsRequired && !string.IsNullOrEmpty(f.Value) && f.Value != SecretMask);

            integrations.Add(new IntegrationStatusModel(
                Provider: integration.Provider,
                Name: integration.Name,
                Description: integration.Description,
                Icon: integration.Icon,
                IsConfigured: isConfigured,
                Fields: fields,
                Category: integration.Category,
                SandboxSteps: integration.SetupSteps?.ToList(),
                SandboxUrl: integration.SignupUrl,
                LogoUrl: integration.LogoUrl));
        }

        // ShowSandboxGuides is now always-on — descriptor catalog has
        // setup steps for every integration that benefits from them.
        return new IntegrationSettingsResult(ShowSandboxGuides: true, integrations);
    }

    private static string MapInputType(SettingDescriptor d) => d.DataType switch
    {
        SettingDataType.Boolean => "toggle",
        SettingDataType.Integer => "number",
        SettingDataType.Url => "url",
        SettingDataType.Secret => "password",
        SettingDataType.Json => "textarea",
        SettingDataType.Enum => "enum",
        _ => "text",
    };
}
