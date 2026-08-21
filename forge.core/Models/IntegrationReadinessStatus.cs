namespace Forge.Core.Models;

/// <summary>
/// Readiness of a single integration relative to the current environment posture
/// (production expects real integrations; dev falls back to mocks).
/// </summary>
public enum IntegrationReadinessStatus
{
    /// <summary>Gating capability is off — the integration isn't needed. No nag.</summary>
    NotNeeded,

    /// <summary>Real credentials are present.</summary>
    Configured,

    /// <summary>Running on the mock implementation (dev/mock posture).</summary>
    Mock,

    /// <summary>Gating capability is ON but the integration is unconfigured in a
    /// production posture — the real implementation would fail. Configure it, or
    /// turn the capability off.</summary>
    Gap,

    /// <summary>Infrastructure integration (no gating capability) that is
    /// unconfigured in production — recommended, not blocking.</summary>
    Optional,
}
