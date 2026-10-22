using Forge.Core.Enums;

namespace Forge.Core.Routing;

/// <summary>
/// What step gating decided for a progress update that went ahead: the mode in force and, when a gate was
/// acknowledged (Warn) or overridden (Block), the reason to log.
/// </summary>
public sealed record StepGateOutcome(StepGatingMode Mode, bool Overridden, string? Reason)
{
    public static StepGateOutcome None { get; } = new(StepGatingMode.Off, false, null);
}
