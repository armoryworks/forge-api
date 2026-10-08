using FluentAssertions;

using Forge.Api.Features.EntityCompleteness;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.EntityCompleteness;

public class GetEntityCompletenessTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly EntityCompletenessEvaluator _evaluator;

    public GetEntityCompletenessTests()
    {
        _evaluator = new EntityCompletenessEvaluator(
            _db,
            new StubCapabilitySnapshotProvider("CAP-MD-PARTS"),
            new PredicateEvaluator());
    }

    private async Task<Part> SeedPartAsync(string partNumber, string? description)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            Description = description,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    [Fact]
    public async Task Handle_MatchesTheBatchAnswerForTheSameEntity()
    {
        _db.EntityCapabilityRequirements.Add(new EntityCapabilityRequirement
        {
            EntityType = "Part",
            CapabilityCode = "CAP-MD-PARTS",
            RequirementId = "description",
            Predicate = """{"type":"fieldPresent","field":"description"}""",
            DisplayNameKey = "parts.description",
            MissingMessageKey = "parts.descriptionMissing",
        });
        await _db.SaveChangesAsync();
        var part = await SeedPartAsync("P-1", null);

        var single = await new GetEntityCompletenessHandler(_evaluator).Handle(
            new GetEntityCompletenessQuery("Part", part.Id), CancellationToken.None);
        var batch = await new GetEntityCompletenessBatchHandler(_evaluator).Handle(
            new GetEntityCompletenessBatchQuery("Part", part.Id.ToString()), CancellationToken.None);

        single.EntityId.Should().Be(part.Id);
        single.Capabilities.Should().ContainSingle(c => c.CapabilityCode == "CAP-MD-PARTS" && !c.Ok);
        batch.Should().ContainSingle().Which.Should().BeEquivalentTo(single);
    }

    [Fact]
    public async Task Handle_UnknownEntity_ThrowsKeyNotFound()
    {
        var act = () => new GetEntityCompletenessHandler(_evaluator).Handle(
            new GetEntityCompletenessQuery("Part", 999999), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
