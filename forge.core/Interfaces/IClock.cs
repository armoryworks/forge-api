namespace Forge.Core.Interfaces;

/// <summary>
/// Abstraction over system time. Inject this instead of DateTimeOffset.UtcNow
/// so the clock can be controlled during simulation and testing.
/// </summary>
public interface IClock
{
    DateTimeOffset UtcNow { get; }
}
