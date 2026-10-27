using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Ai;
using Forge.Core.Entities;
using Forge.Tests.Helpers;

namespace Forge.Tests.Ai;

[Collection(PostgresCollection.Name)]
public sealed class AiIndexStatusTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Status_counts_indexed_records_and_chunks_per_type_against_what_exists()
    {
        await using var db = fixture.CreateContext();
        await db.DocumentEmbeddings.ExecuteDeleteAsync();
        db.DocumentEmbeddings.AddRange(
            new DocumentEmbedding { EntityType = "Job", EntityId = 1, ChunkIndex = 0, ChunkText = "a", ModelName = "m" },
            new DocumentEmbedding { EntityType = "Job", EntityId = 1, ChunkIndex = 1, ChunkText = "b", ModelName = "m" },
            new DocumentEmbedding { EntityType = "Job", EntityId = 2, ChunkIndex = 0, ChunkText = "c", ModelName = "m" },
            new DocumentEmbedding { EntityType = "Documentation", EntityId = 7, ChunkIndex = 0, ChunkText = "d", ModelName = "m" });
        await db.SaveChangesAsync();
        var jobs = await db.Jobs.CountAsync();

        var status = await new GetAiIndexStatusHandler(db).Handle(new GetAiIndexStatusQuery(), CancellationToken.None);

        var job = status.EntityTypes.Single(t => t.EntityType == "Job");
        job.IndexedCount.Should().Be(2);
        job.ChunkCount.Should().Be(3);
        job.TotalCount.Should().Be(jobs);
        job.LastIndexedAt.Should().NotBeNull();
        status.EntityTypes.Single(t => t.EntityType == "Documentation").TotalCount.Should().BeNull();
        status.EntityTypes.Single(t => t.EntityType == "Part").IndexedCount.Should().Be(0);
        status.TotalChunks.Should().Be(4);

        await db.DocumentEmbeddings.ExecuteDeleteAsync();
    }
}
