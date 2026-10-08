using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;

namespace Forge.Api.Features.Discovery.Preview;

public record DiscoveryCapabilityAdjustmentResponseModel(
    string Code,
    string Name,
    bool Enabled,
    string Reason)
{
    public static IReadOnlyList<DiscoveryCapabilityAdjustmentResponseModel> From(
        IReadOnlyList<DiscoveryCapabilityAdjustment> adjustments) =>
        adjustments
            .Select(a => new DiscoveryCapabilityAdjustmentResponseModel(
                a.Code,
                CapabilityCatalog.All.FirstOrDefault(c => c.Code == a.Code)?.Name ?? a.Code,
                a.Enabled,
                a.Reason))
            .ToList();
}
