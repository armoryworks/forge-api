using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Tests.Helpers;

namespace Forge.Tests.Ai;

/// <summary>
/// ai-fleet-orchestration D. The provenance stamper marks an artifact once (idempotent) and
/// reports whether an entity is AI-generated (drives the UI provenance icon).
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class AiProvenanceTests(PostgresFixture fixture)
{
    [Fact]
    public async Task Stamp_marks_entity_once_and_queries()
    {
        await using var db = fixture.CreateContext();
        await db.AiProvenances.ExecuteDeleteAsync();

        var stamper = new AiProvenanceStamper(db);
        await stamper.StampAsync("purchase_order", 42, "gemma3:4b");
        await stamper.StampAsync("purchase_order", 42, "gemma3:4b"); // idempotent

        (await stamper.IsAiGeneratedAsync("purchase_order", 42)).Should().BeTrue();
        (await stamper.IsAiGeneratedAsync("purchase_order", 99)).Should().BeFalse();

        await using var verify = fixture.CreateContext();
        (await verify.AiProvenances.CountAsync(p => p.EntityType == "purchase_order" && p.EntityId == 42)).Should().Be(1);
    }
}
