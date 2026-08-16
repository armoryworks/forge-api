using System.Text.RegularExpressions;

using FluentAssertions;

using Forge.Api.Capabilities;

namespace Forge.Tests.Architecture;

/// <summary>
/// CLAUDE.md is loaded into every session and trusted; a stale factual claim there produces wrong
/// decisions downstream (2026-08-16: it said the FULLGL enable-path was unwired five weeks after
/// it shipped, and an architecture recommendation was made on that basis). Prose can't be tested,
/// but the numbers can — so the ones that drift are asserted against the code they describe.
/// If one of these fails, fix CLAUDE.md, not the test.
/// </summary>
public sealed partial class ClaudeMdFactsTests
{
    private static string ClaudeMd => File.ReadAllText(Path.Combine(RepoRoot.Path, "CLAUDE.md"));

    [GeneratedRegex(@"(\d+) named capabilities")]
    private static partial Regex CapabilityCountClaim();

    [GeneratedRegex(@"\*\*Backend:\*\* \.NET (\d+)")]
    private static partial Regex DotnetVersionClaim();

    [GeneratedRegex(@"<TargetFramework>net(\d+)\.0</TargetFramework>")]
    private static partial Regex TargetFramework();

    [Fact]
    public void Capability_count_in_CLAUDE_md_matches_the_catalog()
    {
        var claim = CapabilityCountClaim().Match(ClaudeMd);
        claim.Success.Should().BeTrue("CLAUDE.md's Capability Gating section states the catalog size as 'N named capabilities'");

        int.Parse(claim.Groups[1].Value).Should().Be(CapabilityCatalog.All.Count,
            "CLAUDE.md claims {0} capabilities but CapabilityCatalog.All has {1} — update the doc", claim.Groups[1].Value, CapabilityCatalog.All.Count);
    }

    [Fact]
    public void Dotnet_version_in_CLAUDE_md_matches_the_csproj()
    {
        var claim = DotnetVersionClaim().Match(ClaudeMd);
        claim.Success.Should().BeTrue("CLAUDE.md's Tech Stack section states '**Backend:** .NET N'");

        var csproj = File.ReadAllText(Path.Combine(RepoRoot.Path, "forge.api", "forge.api.csproj"));
        var actual = TargetFramework().Match(csproj);
        actual.Success.Should().BeTrue("forge.api.csproj declares a <TargetFramework>");

        claim.Groups[1].Value.Should().Be(actual.Groups[1].Value,
            "CLAUDE.md says .NET {0} but forge.api targets net{1}.0 — update the doc", claim.Groups[1].Value, actual.Groups[1].Value);
    }
}
