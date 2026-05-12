namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — Singleton holder for the current
/// <see cref="CapabilitySnapshot"/>. Hydrated at startup after the seeder
/// runs, refreshed on capability mutation events (Phase C).
/// </summary>
public interface ICapabilitySnapshotProvider
{
    /// <summary>
    /// Current snapshot (immutable; safe to retain across calls).
    /// Returns <see cref="CapabilitySnapshot.Empty"/> if not yet hydrated.
    /// </summary>
    CapabilitySnapshot Current { get; }

    /// <summary>Convenience: defers to <see cref="CapabilitySnapshot.IsEnabled(string)"/> on the current snapshot.</summary>
    bool IsEnabled(string code);

    /// <summary>Re-reads capability state from the database and atomically swaps the snapshot.</summary>
    Task RefreshAsync(CancellationToken ct = default);
}
