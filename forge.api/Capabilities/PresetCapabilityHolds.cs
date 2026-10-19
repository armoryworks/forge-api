namespace Forge.Api.Capabilities;

/// <summary>
/// Capabilities a preset or discovery apply does not own. Presets are a full-state sync
/// (anything outside the preset's set is turned off), and no preset lists the general-ledger
/// capabilities, so without these holds re-applying any preset would switch off the built-in
/// ledger, its read-only view and everything the ledger needs.
/// <list type="bullet">
/// <item>CAP-ACCT-GL-VIEW stays on once enabled, as long as the target keeps CAP-ACCT-BUILTIN
/// (which it depends on), so ledger history stays viewable (fullgl-deactivation §3.1).</item>
/// <item>While the ledger is live (CAP-ACCT-FULLGL on and opening balances loaded),
/// CAP-ACCT-FULLGL, CAP-ACCT-GL-VIEW, every enabled capability that depends on FULLGL, and
/// everything those depend on (CAP-ACCT-BUILTIN included) keep their current state. A preset
/// that enables CAP-ACCT-EXTERNAL is then refused on the BUILTIN mutex, which is correct:
/// leaving the built-in ledger needs the governed deactivation cutover.</item>
/// </list>
/// Codes a Custom apply names explicitly are left to the caller's choice; the general-ledger
/// guard still refuses an explicit change that breaks a ledger rule.
/// </summary>
public static class PresetCapabilityHolds
{
    /// <summary>The capability that posts to the native general ledger.</summary>
    public const string FullGl = "CAP-ACCT-FULLGL";

    /// <summary>The read-only ledger view surface.</summary>
    public const string GlView = "CAP-ACCT-GL-VIEW";

    /// <summary>The built-in books GL-VIEW depends on.</summary>
    public const string Builtin = "CAP-ACCT-BUILTIN";

    /// <summary>
    /// Adjusts <paramref name="target"/> in place so the held capabilities keep their
    /// <paramref name="current"/> state.
    /// </summary>
    public static void Apply(
        ISet<string> target,
        IReadOnlyDictionary<string, bool> current,
        bool ledgerLive,
        IReadOnlyCollection<string>? explicitCodes = null)
    {
        var chosen = explicitCodes is null
            ? new HashSet<string>(StringComparer.Ordinal)
            : new HashSet<string>(explicitCodes, StringComparer.Ordinal);

        if (ledgerLive)
        {
            foreach (var code in LiveLedgerHeld(current))
            {
                if (chosen.Contains(code)) continue;
                if (IsOn(current, code)) target.Add(code);
                else target.Remove(code);
            }
        }

        if (!chosen.Contains(GlView) && IsOn(current, GlView) && target.Contains(Builtin))
            target.Add(GlView);
    }

    /// <summary>
    /// The codes held while the ledger is live: FULLGL, GL-VIEW, FULLGL's enabled transitive
    /// dependents, and the dependency closure of all of them.
    /// </summary>
    public static IReadOnlySet<string> LiveLedgerHeld(IReadOnlyDictionary<string, bool> current)
    {
        var held = new HashSet<string>(StringComparer.Ordinal) { FullGl, GlView };

        var frontier = new Queue<string>([FullGl]);
        while (frontier.Count > 0)
        {
            var code = frontier.Dequeue();
            foreach (var edge in CapabilityCatalogRelations.Dependencies)
            {
                if (edge.To != code || !IsOn(current, edge.From)) continue;
                if (held.Add(edge.From)) frontier.Enqueue(edge.From);
            }
        }

        var needs = new Queue<string>(held);
        while (needs.Count > 0)
        {
            var code = needs.Dequeue();
            foreach (var edge in CapabilityCatalogRelations.Dependencies)
            {
                if (edge.From != code) continue;
                if (held.Add(edge.To)) needs.Enqueue(edge.To);
            }
        }

        return held;
    }

    private static bool IsOn(IReadOnlyDictionary<string, bool> state, string code) =>
        state.TryGetValue(code, out var on) && on;
}
