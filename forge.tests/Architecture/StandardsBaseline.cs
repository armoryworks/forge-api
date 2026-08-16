using System.Text.Json;

namespace Forge.Tests.Architecture;

/// <summary>
/// Per-file ratchet baseline for source-standards rules that have legacy debt too large to fix in
/// one commit. Each rule maps repo-relative file path → violation count as of the last accepted
/// baseline. The tests enforce, per rule:
/// <list type="bullet">
/// <item>a file NOT in the baseline may have <b>zero</b> violations (new code follows the rule);</item>
/// <item>a file IN the baseline may not exceed its recorded count (debt never grows);</item>
/// <item>a file whose count fell, or which no longer exists, must have its entry lowered/removed
/// (the ratchet only tightens — the baseline is a debt register, not a permission slip).</item>
/// </list>
/// To accept an improvement, rerun with <c>FORGE_STANDARDS_UPDATE_BASELINE=1</c>; the test rewrites
/// <c>standards-baseline.json</c> to the current state and passes. Commit that file with the change.
/// The baseline is <b>never</b> loosened by hand — if a rule genuinely needs an exemption, that is a
/// conversation about the rule, recorded in CLAUDE.md, not a bigger number here.
/// </summary>
internal sealed class StandardsBaseline
{
    public const string UpdateEnvVar = "FORGE_STANDARDS_UPDATE_BASELINE";

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true };

    private readonly string _path;
    private readonly SortedDictionary<string, SortedDictionary<string, int>> _rules;

    private StandardsBaseline(string path, SortedDictionary<string, SortedDictionary<string, int>> rules)
    {
        _path = path;
        _rules = rules;
    }

    public static StandardsBaseline Load()
    {
        var path = Path.Combine(RepoRoot.Path, "forge.tests", "Architecture", "standards-baseline.json");
        var rules = File.Exists(path)
            ? JsonSerializer.Deserialize<SortedDictionary<string, SortedDictionary<string, int>>>(File.ReadAllText(path), Json)
              ?? new SortedDictionary<string, SortedDictionary<string, int>>()
            : new SortedDictionary<string, SortedDictionary<string, int>>();
        return new StandardsBaseline(path, rules);
    }

    public static bool UpdateRequested =>
        Environment.GetEnvironmentVariable(UpdateEnvVar) is "1" or "true";

    /// <summary>
    /// Compares <paramref name="current"/> (file → count, zero-count files omitted) against the
    /// baseline for <paramref name="rule"/>. Returns the list of human-readable failures; empty means
    /// the ratchet holds. In update mode the baseline is rewritten instead and no failures return.
    /// </summary>
    public IReadOnlyList<string> Check(string rule, IReadOnlyDictionary<string, int> current)
    {
        _rules.TryGetValue(rule, out var recorded);
        recorded ??= new SortedDictionary<string, int>();

        if (UpdateRequested)
        {
            _rules[rule] = new SortedDictionary<string, int>(current.Where(kv => kv.Value > 0).ToDictionary(kv => kv.Key, kv => kv.Value));
            File.WriteAllText(_path, JsonSerializer.Serialize(_rules, Json) + "\n");
            return [];
        }

        var failures = new List<string>();

        foreach (var (file, count) in current.Where(kv => kv.Value > 0))
        {
            if (!recorded.TryGetValue(file, out var allowed))
                failures.Add($"NEW VIOLATION  {file}: {count} — this file is not in the baseline; new code must follow the rule.");
            else if (count > allowed)
                failures.Add($"DEBT GREW      {file}: {count} > baseline {allowed}.");
            else if (count < allowed)
                failures.Add($"RATCHET DOWN   {file}: {count} < baseline {allowed} — nice; rerun with {UpdateEnvVar}=1 and commit the baseline.");
        }

        foreach (var (file, allowed) in recorded)
        {
            if (!current.TryGetValue(file, out var count) || count == 0)
                failures.Add($"STALE ENTRY    {file}: baseline says {allowed}, now clean/gone — rerun with {UpdateEnvVar}=1 and commit the baseline.");
        }

        return failures;
    }
}
