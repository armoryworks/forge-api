namespace Forge.Core.Models;

/// <summary>Readiness verdict for one integration, joining its configured state
/// with the capability that makes it needed and the environment posture.</summary>
public sealed record IntegrationReadiness(
    string Provider,
    string Name,
    string? CapabilityCode,
    bool CapabilityEnabled,
    bool IsConfigured,
    IntegrationReadinessStatus Status);
