namespace Forge.Core.Models;

/// <summary>
/// Where this install reports health, when its operator has opted in. Configuration
/// rather than a constant so a self-hosted install can point it somewhere else or
/// nowhere, and so tests aren't obliged to talk to production.
///
/// An empty <see cref="Endpoint"/> disables the feature outright: the settings screen
/// says monitoring is unavailable and the opt-in cannot be switched on. That is the
/// right default for a Forge someone downloaded and runs themselves — there is no
/// vendor on the other end to report to.
/// </summary>
public class TelemetryOptions
{
    public const string SectionName = "Telemetry";

    /// <summary>Base URL of the control plane, e.g. https://tuyere.armoryworks.com. Empty disables monitoring.</summary>
    public string Endpoint { get; set; } = "";

    /// <summary>
    /// Public URL of THIS install, when it has one. Sent at enrollment so Armory Works
    /// can also probe it from outside; omitted for an install with no public address,
    /// which can then only be known by what it reports.
    /// </summary>
    public string PublicUrl { get; set; } = "";
}
