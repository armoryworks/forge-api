using FluentAssertions;

using Forge.Api.Features.EntityCompleteness;
using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.EntityCompleteness;

public class GetEntityCompletenessBatchTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly GetEntityCompletenessBatchHandler _handler;

    public GetEntityCompletenessBatchTests()
    {
        _handler = new GetEntityCompletenessBatchHandler(
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

    private async Task SeedRequirementAsync(string capabilityCode)
    {
        _db.EntityCapabilityRequirements.Add(new EntityCapabilityRequirement
        {
            EntityType = "Part",
            CapabilityCode = capabilityCode,
            RequirementId = "description",
            Predicate = """{"type":"fieldPresent","field":"description"}""",
            DisplayNameKey = "parts.description",
            MissingMessageKey = "parts.descriptionMissing",
        });
        await _db.SaveChangesAsync();
    }

    [Fact]
    public async Task Handle_ReturnsOneEntryPerId_InRequestOrder()
    {
        await SeedRequirementAsync("CAP-MD-PARTS");
        var complete = await SeedPartAsync("P-1", "Has a description");
        var incomplete = await SeedPartAsync("P-2", null);

        var result = await _handler.Handle(
            new GetEntityCompletenessBatchQuery("Part", $"{incomplete.Id},{complete.Id}"),
            CancellationToken.None);

        result.Select(r => r.EntityId).Should().Equal(incomplete.Id, complete.Id);
        result.Should().AllSatisfy(r => r.EntityType.Should().Be("Part"));
        result[0].Capabilities.Should().ContainSingle(c => c.CapabilityCode == "CAP-MD-PARTS" && !c.Ok)
            .Which.MissingFields.Should().ContainSingle(f => f.RequirementId == "description");
        result[1].Capabilities.Should().ContainSingle(c => c.CapabilityCode == "CAP-MD-PARTS" && c.Ok);
    }

    [Fact]
    public async Task Handle_SkipsIdsThatDoNotResolve()
    {
        var part = await SeedPartAsync("P-1", null);

        var result = await _handler.Handle(
            new GetEntityCompletenessBatchQuery("Part", $"{part.Id},999999"),
            CancellationToken.None);

        result.Should().ContainSingle().Which.EntityId.Should().Be(part.Id);
    }

    [Fact]
    public async Task Handle_CollapsesDuplicateIds()
    {
        var part = await SeedPartAsync("P-1", null);

        var result = await _handler.Handle(
            new GetEntityCompletenessBatchQuery("Part", $"{part.Id}, {part.Id}"),
            CancellationToken.None);

        result.Should().ContainSingle();
    }

    [Fact]
    public async Task Handle_IgnoresRequirementsOfDisabledCapabilities()
    {
        await SeedRequirementAsync("CAP-P2P-PO");
        var part = await SeedPartAsync("P-1", null);

        var result = await _handler.Handle(
            new GetEntityCompletenessBatchQuery("Part", part.Id.ToString()),
            CancellationToken.None);

        result.Should().ContainSingle().Which.Capabilities.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_UnknownEntityType_ReturnsEmpty()
    {
        var part = await SeedPartAsync("P-1", null);

        var result = await _handler.Handle(
            new GetEntityCompletenessBatchQuery("Spaceship", part.Id.ToString()),
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    [Theory]
    [InlineData("1,2,3", true)]
    [InlineData("", false)]
    [InlineData("1,abc", false)]
    [InlineData("0", false)]
    public void Validator_AcceptsOnlyPositiveIdLists(string ids, bool valid)
    {
        var result = new GetEntityCompletenessBatchValidator()
            .Validate(new GetEntityCompletenessBatchQuery("Part", ids));

        result.IsValid.Should().Be(valid);
    }

    [Fact]
    public void Validator_RejectsMoreThanTheCap()
    {
        var ids = string.Join(',', Enumerable.Range(1, GetEntityCompletenessBatchValidator.MaxIds + 1));

        var result = new GetEntityCompletenessBatchValidator()
            .Validate(new GetEntityCompletenessBatchQuery("Part", ids));

        result.IsValid.Should().BeFalse();
    }
}
