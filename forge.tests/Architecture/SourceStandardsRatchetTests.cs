using System.Text.RegularExpressions;

using FluentAssertions;

namespace Forge.Tests.Architecture;

/// <summary>
/// Promotes three CLAUDE.md rules from prose to a failing test, with a per-file ratchet
/// (<see cref="StandardsBaseline"/>) because the legacy debt is too large to fix in one commit:
/// <list type="number">
/// <item><b>IClock over DateTime.UtcNow</b> — handlers/services must inject <c>IClock</c>
/// (testable time, drives the E2E SimulationClock). Scope: <c>forge.api/Features</c>,
/// <c>forge.api/Services</c>, <c>forge.api/Jobs</c>. <c>SystemClock</c> itself is the one legitimate home.</item>
/// <item><b>No try/catch in controllers</b> — the exception middleware maps exceptions to Problem
/// Details; a controller catch hides that contract.</item>
/// <item><b>One object per file</b> — the CQRS shape (request record + result record + handler +
/// validator) is sanctioned, so the tripwire is <b>five or more</b> top-level type declarations.</item>
/// </list>
/// New files must be clean; baselined files may not get worse; improvements must be recorded
/// (<c>FORGE_STANDARDS_UPDATE_BASELINE=1 dotnet test --filter Architecture</c>).
/// </summary>
public sealed partial class SourceStandardsRatchetTests
{
    [GeneratedRegex(@"\bDateTime(Offset)?\.UtcNow\b")]
    private static partial Regex UtcNowRe();

    [GeneratedRegex(@"^\s*try\s*(\{|$)", RegexOptions.Multiline)]
    private static partial Regex TryBlockRe();

    [GeneratedRegex(@"^(public|internal)\s+(sealed\s+|static\s+|abstract\s+|partial\s+|readonly\s+)*(class|record\s+struct|record|interface|enum|struct)\s+\w+", RegexOptions.Multiline)]
    private static partial Regex TopLevelTypeRe();

    [Fact]
    public void Handlers_and_services_use_IClock_not_DateTime_UtcNow()
    {
        var current = new Dictionary<string, int>();
        foreach (var dir in new[] { "forge.api/Features", "forge.api/Services", "forge.api/Jobs" })
            foreach (var (rel, full) in RepoRoot.SourceFiles(dir))
            {
                var n = UtcNowRe().Matches(File.ReadAllText(full)).Count;
                if (n > 0) current[rel] = n;
            }

        AssertRatchet("datetime-utcnow-outside-iclock", current);
    }

    [Fact]
    public void Controllers_do_not_catch_exceptions()
    {
        var current = new Dictionary<string, int>();
        foreach (var (rel, full) in RepoRoot.SourceFiles("forge.api/Controllers"))
        {
            var n = TryBlockRe().Matches(File.ReadAllText(full)).Count;
            if (n > 0) current[rel] = n;
        }

        AssertRatchet("try-catch-in-controllers", current);
    }

    [Fact]
    public void Files_do_not_pile_up_top_level_types()
    {
        const int threshold = 5;
        var current = new Dictionary<string, int>();
        foreach (var dir in new[] { "forge.api/Features", "forge.api/Services", "forge.core" })
            foreach (var (rel, full) in RepoRoot.SourceFiles(dir))
            {
                var n = TopLevelTypeRe().Matches(File.ReadAllText(full)).Count;
                if (n >= threshold) current[rel] = n;
            }

        AssertRatchet("five-or-more-types-per-file", current);
    }

    private static void AssertRatchet(string rule, Dictionary<string, int> current)
    {
        var failures = StandardsBaseline.Load().Check(rule, current);
        failures.Should().BeEmpty(
            $"rule '{rule}' is a ratchet — see forge.tests/Architecture/StandardsBaseline.cs. Failures:\n  " +
            string.Join("\n  ", failures));
    }
}
