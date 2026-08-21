using Forge.Api.Capabilities;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Api.Services;

/// <inheritdoc cref="IIntegrationReadinessService"/>
public sealed class IntegrationReadinessService(
    ISettingsService settings,
    ICapabilitySnapshotProvider capabilities,
    IWebHostEnvironment environment,
    IConfiguration configuration) : IIntegrationReadinessService
{
    public async Task<IntegrationReadinessReport> BuildAsync(CancellationToken ct = default)
    {
        var globalMocks = configuration.GetValue<bool>("MockIntegrations");
        var isProduction = environment.IsProduction();
        // Production posture = a production environment NOT globally forced to mock.
        var productionPosture = isProduction && !globalMocks;

        var list = new List<IntegrationReadiness>(IntegrationDescriptorCatalog.All.Count);
        foreach (var d in IntegrationDescriptorCatalog.All)
        {
            var capabilityEnabled = d.CapabilityCode is null || capabilities.IsEnabled(d.CapabilityCode);

            var isConfigured = false;
            if (d.IsConfiguredCheckKey is not null)
            {
                var value = await settings.GetStringAsync(d.IsConfiguredCheckKey, ct);
                isConfigured = !string.IsNullOrWhiteSpace(value);
            }

            var status = Classify(d, capabilityEnabled, isConfigured, productionPosture);
            list.Add(new IntegrationReadiness(
                d.Provider, d.Name, d.CapabilityCode, capabilityEnabled, isConfigured, status));
        }

        return new IntegrationReadinessReport(
            ProductionPosture: productionPosture,
            MockIntegrationsInProduction: isProduction && globalMocks,
            Integrations: list);
    }

    private static IntegrationReadinessStatus Classify(
        IntegrationDescriptor d, bool capabilityEnabled, bool isConfigured, bool productionPosture)
    {
        // A gated integration whose capability is off is simply not needed — no nag.
        if (d.CapabilityCode is not null && !capabilityEnabled)
            return IntegrationReadinessStatus.NotNeeded;

        if (isConfigured)
            return IntegrationReadinessStatus.Configured;

        // Unconfigured below. In a non-production posture the mock impl covers it.
        if (!productionPosture)
            return IntegrationReadinessStatus.Mock;

        // Production posture + unconfigured: a gated integration is an actionable gap;
        // infrastructure (no capability) is recommended but not blocking.
        return d.CapabilityCode is not null
            ? IntegrationReadinessStatus.Gap
            : IntegrationReadinessStatus.Optional;
    }
}
