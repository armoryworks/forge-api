using Forge.Core.Interfaces;

namespace Forge.Integrations;

/// <summary>Real-time clock — delegates to DateTimeOffset.UtcNow.</summary>
public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;
}
