using FluentAssertions;

using Forge.Api.Features.Checkpoints;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Checkpoints;

[Collection(PostgresCollection.Name)]
public sealed class CheckpointHandlerTests(PostgresFixture fixture)
{
    [Fact]
    public async Task GetCheckpoint_UnknownWorldId_ReturnsNull()
    {
        await using var db = fixture.CreateContext();

        var result = await new GetCheckpointHandler(db)
            .Handle(new GetCheckpointQuery("CheckpointHandlerTests-never-put"), default);

        result.Should().BeNull();
    }

    [Fact]
    public async Task Upsert_ThenGet_RoundTripsTheBlobVerbatim()
    {
        const string worldId = "CheckpointHandlerTests-roundtrip";
        const string blob = """{"tick":1298,"hash":"0xd9c07069dab5830d","inserters":[{"id":0,"state":"Blocked"}]}""";

        await using (var write = fixture.CreateContext())
        {
            await new UpsertCheckpointHandler(write).Handle(new UpsertCheckpointCommand(worldId, blob), default);
        }

        await using var read = fixture.CreateContext();
        var result = await new GetCheckpointHandler(read).Handle(new GetCheckpointQuery(worldId), default);

        result.Should().NotBeNull();
        result!.WorldId.Should().Be(worldId);
        result.Blob.Should().Be(blob);
    }

    [Fact]
    public async Task Upsert_SecondCallSameWorldId_OverwritesRatherThanDuplicates()
    {
        const string worldId = "CheckpointHandlerTests-overwrite";

        await using (var first = fixture.CreateContext())
        {
            await new UpsertCheckpointHandler(first).Handle(new UpsertCheckpointCommand(worldId, "{\"tick\":1}"), default);
        }
        await using (var second = fixture.CreateContext())
        {
            await new UpsertCheckpointHandler(second).Handle(new UpsertCheckpointCommand(worldId, "{\"tick\":2}"), default);
        }

        await using var read = fixture.CreateContext();
        var rows = read.Checkpoints.Where(c => c.WorldId == worldId).ToList();

        rows.Should().HaveCount(1, "the unique index on world_id must make this an upsert, not an insert");
        rows[0].Blob.Should().Be("{\"tick\":2}");
    }
}
