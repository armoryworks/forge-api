using System.Text.Json;

using FluentAssertions;

using Forge.Api.Capabilities;
using Forge.Api.Data;
using Forge.Tests.Helpers;

namespace Forge.Tests.Architecture;

/// <summary>
/// Every capability in <see cref="CapabilityCatalog"/> must be claimed by at
/// least one training seeder (<c>TrainingContentBase.Capabilities</c>). The
/// untaught set on the day this landed lives in
/// <c>training-coverage-baseline.json</c> and may only shrink: a new capability
/// shipped without a module fails the build, and a capability that gains a
/// module must be removed from the baseline in the same commit
/// (<c>FORGE_STANDARDS_UPDATE_BASELINE=1</c> rewrites it).
/// </summary>
public sealed class TrainingCoverageRatchetTests
{
    private static readonly string BaselinePath =
        Path.Combine(RepoRoot.Path, "forge.tests", "Architecture", "training-coverage-baseline.json");

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private static IReadOnlyList<string> ClaimedCapabilities()
    {
        using var db = TestDbContextFactory.Create();
        return SeedData.CreateSeeders(db, new Dictionary<string, int>())
            .SelectMany(s => s.Capabilities)
            .Distinct()
            .ToList();
    }

    [Fact]
    public void Seeders_only_claim_capabilities_that_exist()
    {
        var known = CapabilityCatalog.All.Select(c => c.Code).ToHashSet();
        ClaimedCapabilities().Where(c => !known.Contains(c))
            .Should().BeEmpty("a training seeder claims a capability code that is not in the catalog");
    }

    [Fact]
    public void Every_capability_has_training_or_is_in_the_baseline()
    {
        var claimed = ClaimedCapabilities().ToHashSet();
        var untaught = CapabilityCatalog.All.Select(c => c.Code).Where(c => !claimed.Contains(c)).Order().ToList();

        if (Environment.GetEnvironmentVariable(StandardsBaseline.UpdateEnvVar) == "1")
        {
            File.WriteAllText(BaselinePath, JsonSerializer.Serialize(untaught, JsonOptions) + "\n");
            return;
        }

        var baseline = File.Exists(BaselinePath)
            ? JsonSerializer.Deserialize<List<string>>(File.ReadAllText(BaselinePath)) ?? []
            : [];

        var newGaps = untaught.Except(baseline).ToList();
        newGaps.Should().BeEmpty(
            "these capabilities shipped without a training module — add a *Training.cs seeder (or claim them from an existing one) before merging:\n  " +
            string.Join("\n  ", newGaps));

        var improved = baseline.Except(untaught).ToList();
        improved.Should().BeEmpty(
            $"RATCHET DOWN — these now have training; rerun with {StandardsBaseline.UpdateEnvVar}=1 to shrink the baseline and commit it:\n  " +
            string.Join("\n  ", improved));
    }
}
