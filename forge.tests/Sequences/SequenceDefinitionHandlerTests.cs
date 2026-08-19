using FluentAssertions;

using Forge.Api.Features.Sequences;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Tests.Sequences;

public class SequenceDefinitionHandlerTests
{
    private static SequenceDefinitionRequestModel Serial(string code = "job-basic") => new(code, "Basic routing", null, "Job",
        [new("cut", "Cut", null, 0), new("inspect", "Inspect", null, 1), new("ship", "Ship", null, 2)],
        [new("cut", "inspect"), new("inspect", "ship")],
        [new("inspect", "qc", "First article", SequenceGateSourceType.ManualClearance)]);

    [Fact]
    public async Task Create_publish_new_version_and_retire_follow_the_lifecycle()
    {
        await using var f = new SequenceHandlerFixture();
        var v1 = await new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(Serial()), default);
        v1.Version.Should().Be(1); v1.Status.Should().Be(SequenceDefinitionStatus.Draft); v1.Steps.Should().HaveCount(3);

        var published = await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(v1.Id, SequenceHandlerFixture.UserId), default);
        published.Status.Should().Be(SequenceDefinitionStatus.Published);
        published.PublishedAt.Should().Be(f.Clock.UtcNow);

        // published is immutable
        var edit = () => new UpdateSequenceDefinitionHandler(f.Db).Handle(new UpdateSequenceDefinitionCommand(v1.Id, Serial()), default);
        await edit.Should().ThrowAsync<InvalidOperationException>();

        var v2 = await new NewSequenceDefinitionVersionHandler(f.Db).Handle(new NewSequenceDefinitionVersionCommand(v1.Id), default);
        v2.Version.Should().Be(2); v2.Status.Should().Be(SequenceDefinitionStatus.Draft); v2.Gates.Should().HaveCount(1);

        // publishing v2 retires v1
        await new PublishSequenceDefinitionHandler(f.Db, f.Clock).Handle(new PublishSequenceDefinitionCommand(v2.Id, SequenceHandlerFixture.UserId), default);
        (await new GetSequenceDefinitionHandler(f.Db).Handle(new GetSequenceDefinitionQuery(v1.Id), default)).Status.Should().Be(SequenceDefinitionStatus.Retired);

        var all = await new GetSequenceDefinitionsHandler(f.Db).Handle(new GetSequenceDefinitionsQuery("job-basic"), default);
        all.Select(d => d.Version).Should().Equal(2, 1);
    }

    [Fact]
    public async Task Create_rejects_a_structurally_invalid_graph()
    {
        await using var f = new SequenceHandlerFixture();
        var bad = Serial() with { Edges = [new("cut", "inspect"), new("inspect", "cut")] }; // non-rework cycle
        var act = () => new CreateSequenceDefinitionHandler(f.Db).Handle(new CreateSequenceDefinitionCommand(bad), default);
        (await act.Should().ThrowAsync<InvalidOperationException>()).Which.Message.Should().Contain("Invalid sequence definition");
    }
}
