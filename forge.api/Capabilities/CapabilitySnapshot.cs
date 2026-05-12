namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-A — Frozen point-in-time view of capability state.
/// One instance is held by <see cref="ICapabilitySnapshotProvider"/> and
/// shared across requests by reference. Re-built (atomically swapped) on
/// every capability mutation event in Phase C.
/// </summary>
public sealed class CapabilitySnapshot
{
    public CapabilitySnapshot(IReadOnlyDictionary<string, bool> enabledByCode, DateTimeOffset generatedAt)
    {
        EnabledByCode = enabledByCode;
        GeneratedAt = generatedAt;
    }

    /// <summary>Dictionary keyed by capability code (e.g. "CAP-MD-CUSTOMERS"). True = enabled, false = disabled.</summary>
    public IReadOnlyDictionary<string, bool> EnabledByCode { get; }

    /// <summary>UTC timestamp when this snapshot was hydrated. Used in the descriptor response for cache-validation.</summary>
    public DateTimeOffset GeneratedAt { get; }

    public bool IsEnabled(string code)
        => EnabledByCode.TryGetValue(code, out var enabled) && enabled;

    public static CapabilitySnapshot Empty { get; } = new(
        new Dictionary<string, bool>(StringComparer.Ordinal),
        DateTimeOffset.MinValue);
}
