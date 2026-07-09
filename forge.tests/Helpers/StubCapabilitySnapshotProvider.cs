using Forge.Api.Capabilities;

namespace Forge.Tests.Helpers;

/// <summary>
/// Test double for <see cref="ICapabilitySnapshotProvider"/> — enables exactly the capability codes it's
/// constructed with, everything else disabled. Handy for unit-testing services that branch on a capability
/// (e.g. the Tier-2 departmental cost rollup) without spinning up the full capability seeder.
/// </summary>
public sealed class StubCapabilitySnapshotProvider(params string[] enabled) : ICapabilitySnapshotProvider
{
    private readonly HashSet<string> _enabled = new(enabled, StringComparer.Ordinal);

    public CapabilitySnapshot Current
        => new(_enabled.ToDictionary(c => c, _ => true, StringComparer.Ordinal), DateTimeOffset.UnixEpoch);

    public bool IsEnabled(string code) => _enabled.Contains(code);

    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;

    /// <summary>Nothing enabled — forces Tier-1 flat costing.</summary>
    public static readonly StubCapabilitySnapshotProvider Off = new();

    /// <summary>Tier-2 departmental rates capability on.</summary>
    public static readonly StubCapabilitySnapshotProvider Tier2On = new("CAP-COSTING-TIER2-DEPTRATES");
}
