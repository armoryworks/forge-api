using FluentAssertions;

using Forge.Api.Services;
using Forge.Core.Enums;

namespace Forge.Tests.Ai;

/// <summary>
/// ai-fleet-orchestration D-2. The client override directory shadows same-named baseline docs
/// and adds client-only docs. Pure file-path logic — no DB/AI.
/// </summary>
public sealed class ClientDocResolverTests
{
    [Fact]
    public void Client_override_shadows_baseline_and_adds_new()
    {
        var baseDir = Directory.CreateTempSubdirectory("forge-base").FullName;
        var clientDir = Directory.CreateTempSubdirectory("forge-client").FullName;
        try
        {
            File.WriteAllText(Path.Combine(baseDir, "a.md"), "baseline a");
            File.WriteAllText(Path.Combine(baseDir, "b.md"), "baseline b");
            File.WriteAllText(Path.Combine(clientDir, "a.md"), "client a");   // override
            File.WriteAllText(Path.Combine(clientDir, "c.md"), "client c");   // new

            var result = new ClientDocResolver().Resolve(baseDir, clientDir);

            result.Should().HaveCount(3);
            result.Single(r => r.RelativePath == "a.md").Source.Should().Be(DocSource.Client);
            result.Single(r => r.RelativePath == "b.md").Source.Should().Be(DocSource.Baseline);
            result.Single(r => r.RelativePath == "c.md").Source.Should().Be(DocSource.Client);
        }
        finally
        {
            Directory.Delete(baseDir, true);
            Directory.Delete(clientDir, true);
        }
    }

    [Fact]
    public void Missing_client_dir_falls_back_to_baseline_only()
    {
        var baseDir = Directory.CreateTempSubdirectory("forge-base2").FullName;
        try
        {
            File.WriteAllText(Path.Combine(baseDir, "only.md"), "x");
            var result = new ClientDocResolver().Resolve(baseDir, "/no/such/dir");
            result.Should().ContainSingle().Which.Source.Should().Be(DocSource.Baseline);
        }
        finally
        {
            Directory.Delete(baseDir, true);
        }
    }
}
