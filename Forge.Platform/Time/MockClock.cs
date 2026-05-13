namespace Forge.Platform.Time;

/// <summary>
/// Controllable clock for simulation and testing.
/// Call <see cref="Set"/> to move the clock to a specific point in time.
/// Thread-safe via lock.
/// </summary>
public sealed class MockClock : IClock
{
    private readonly object _lock = new();
    private DateTimeOffset _now = DateTimeOffset.UtcNow;

    public DateTimeOffset UtcNow { get { lock (_lock) return _now; } }

    /// <summary>Sets the simulated current time.</summary>
    public void Set(DateTimeOffset now) { lock (_lock) _now = now; }

    /// <summary>Advances the clock by the given duration.</summary>
    public void Advance(TimeSpan duration) { lock (_lock) _now = _now.Add(duration); }
}
