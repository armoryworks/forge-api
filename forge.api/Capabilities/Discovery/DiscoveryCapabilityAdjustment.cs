namespace Forge.Api.Capabilities.Discovery;

/// <summary>
/// A capability the answers switch on or off after the chosen preset is
/// applied, with the plain-language reason shown in the preview.
/// </summary>
public record DiscoveryCapabilityAdjustment(string Code, bool Enabled, string Reason);
