namespace Forge.Api.Capabilities;

/// <summary>
/// Phase 4 Phase-C — Pure-function helper that evaluates dependency cascade
/// and soft-mutex constraints from <see cref="CapabilityCatalogRelations"/>
/// against a candidate state map.
///
/// Used by:
///   • <c>ToggleCapabilityHandler</c> — single-row enable / disable.
///   • <c>BulkToggleCapabilitiesHandler</c> — whole-set validation per
///     Phase C decision (validate ALL constraints across the bulk delta
///     before applying anything; preset-apply in Phase G is the canonical
///     consumer).
///
/// Logging:
///   • Catalog drift (an edge that points at a code missing from
///     <see cref="CapabilityCatalog.All"/>) is logged once at startup via
///     <see cref="ValidateGraph(IReadOnlyDictionary{string, CapabilityDefinition}, ILogger)"/>
///     and then silently skipped on every subsequent evaluation. The seeder
///     remains bootable even when the catalog is mid-drift.
/// </summary>
public static class CapabilityDependencyResolver
{
    /// <summary>
    /// Returns the set of currently-enabled capabilities that DEPEND ON the
    /// supplied capability. Used to block disable: 4D-decisions-log #7
    /// "block with informative error" — the admin must disable dependents
    /// first, no silent cascade.
    /// </summary>
    public static IReadOnlyList<string> FindEnabledDependents(string capability, IReadOnlyDictionary<string, bool> enabled)
    {
        var dependents = new List<string>();
        foreach (var edge in CapabilityCatalogRelations.Dependencies)
        {
            if (edge.To != capability) continue;
            if (!enabled.TryGetValue(edge.From, out var isEnabled)) continue;
            if (isEnabled) dependents.Add(edge.From);
        }
        // Stable order for human-friendly messages and for test determinism.
        dependents.Sort(StringComparer.Ordinal);
        return dependents;
    }

    /// <summary>
    /// Returns the dependencies of the supplied capability that are currently
    /// disabled. Used to block enable: the admin must enable required
    /// dependencies first.
    /// </summary>
    public static IReadOnlyList<string> FindMissingDependencies(string capability, IReadOnlyDictionary<string, bool> enabled)
    {
        var missing = new List<string>();
        foreach (var edge in CapabilityCatalogRelations.Dependencies)
        {
            if (edge.From != capability) continue;
            if (enabled.TryGetValue(edge.To, out var isEnabled) && isEnabled) continue;
            missing.Add(edge.To);
        }
        missing.Sort(StringComparer.Ordinal);
        return missing;
    }

    /// <summary>
    /// Returns the soft-mutex peers of the supplied capability that are
    /// currently enabled. Used to block enable: the admin must disable the
    /// peer first.
    /// </summary>
    public static IReadOnlyList<string> FindEnabledMutexConflicts(string capability, IReadOnlyDictionary<string, bool> enabled)
    {
        var conflicts = new List<string>();
        foreach (var edge in CapabilityCatalogRelations.Mutexes)
        {
            string? peer = edge.From == capability
                ? edge.To
                : edge.To == capability
                    ? edge.From
                    : null;
            if (peer is null) continue;
            if (enabled.TryGetValue(peer, out var isEnabled) && isEnabled)
                conflicts.Add(peer);
        }
        conflicts.Sort(StringComparer.Ordinal);
        return conflicts;
    }

    /// <summary>
    /// Phase C startup hook — emits warnings for any edge whose endpoints
    /// don't appear in <see cref="CapabilityCatalog"/>. Returns the count of
    /// dropped edges so callers (the seeder) can include it in their log
    /// summary. The graph remains usable; the bad edges are silently skipped
    /// at evaluation time.
    /// </summary>
    public static int ValidateGraph(IReadOnlyDictionary<string, CapabilityDefinition> catalogByCode, ILogger logger)
    {
        var dropped = 0;
        foreach (var edge in CapabilityCatalogRelations.Dependencies)
        {
            if (!catalogByCode.ContainsKey(edge.From) || !catalogByCode.ContainsKey(edge.To))
            {
                logger.LogWarning(
                    "[CAPABILITY-CATALOG] Dependency edge references unknown capability: {From} -> {To}. Skipping at evaluation time.",
                    edge.From, edge.To);
                dropped++;
            }
        }
        foreach (var edge in CapabilityCatalogRelations.Mutexes)
        {
            if (!catalogByCode.ContainsKey(edge.From) || !catalogByCode.ContainsKey(edge.To))
            {
                logger.LogWarning(
                    "[CAPABILITY-CATALOG] Mutex edge references unknown capability: {From} <-> {To}. Skipping at evaluation time.",
                    edge.From, edge.To);
                dropped++;
            }
        }
        return dropped;
    }
}
