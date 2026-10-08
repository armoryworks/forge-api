using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class CopyRoutingTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly CopyRoutingHandler _handler;

    public CopyRoutingTests()
    {
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(r => r.ResolveAsync(It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int partId, int? _, decimal? _, CancellationToken _) =>
                new ResolvedPartPrice(partId, 0m, "USD", PartPriceSource.Default, null, null));
        _handler = new CopyRoutingHandler(_db, new PartRepository(_db, pricing.Object));
    }

    private async Task<Part> SeedPartAsync(string partNumber, InventoryClass inventoryClass = InventoryClass.Subassembly)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = inventoryClass,
            Status = PartStatus.Active,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<(Part Source, Part Bolt, List<Operation> Ops)> SeedSourceAsync()
    {
        var source = await SeedPartAsync("ASM-00007");
        var bolt = await SeedPartAsync("PRT-00001", InventoryClass.Component);
        var plate = await SeedPartAsync("PRT-00002", InventoryClass.Component);

        var bom = new List<BOMLine>
        {
            new() { ParentPartId = source.Id, ChildPartId = bolt.Id, Quantity = 4, SortOrder = 1 },
            new() { ParentPartId = source.Id, ChildPartId = plate.Id, Quantity = 1, SortOrder = 2 },
        };
        _db.BOMLines.AddRange(bom);
        await _db.SaveChangesAsync();

        var ops = Enumerable.Range(1, 9)
            .Select(step => new Operation
            {
                PartId = source.Id,
                StepNumber = step * 10,
                Title = $"Step {step}",
                WorkCenterId = 100 + step,
                EstimatedMs = step * 8_500,
                SetupMinutes = step,
                RunMinutesLot = step * 3,
                IsQcCheckpoint = step == 9,
            })
            .Reverse()
            .ToList();
        _db.Operations.AddRange(ops);
        await _db.SaveChangesAsync();

        var ordered = ops.OrderBy(o => o.StepNumber).ToList();
        ordered[8].ReferencedOperationId = ordered[1].Id;
        _db.OperationMaterials.Add(new OperationMaterial { OperationId = ordered[2].Id, BomLineId = bom[0].Id, Quantity = 4, Notes = "Torque to spec" });
        _db.OperationMaterials.Add(new OperationMaterial { OperationId = ordered[3].Id, BomLineId = bom[1].Id, Quantity = 1 });
        await _db.SaveChangesAsync();

        return (source, bolt, ordered);
    }

    [Fact]
    public async Task Copy_CopiesEveryOperationInStepOrder_WithRemappedReferences()
    {
        var (source, _, sourceOps) = await SeedSourceAsync();
        var target = await SeedPartAsync("ASM-00008");

        var result = await _handler.Handle(new CopyRoutingCommand(target.Id, source.Id), CancellationToken.None);

        result.Should().HaveCount(9);
        var ops = await _db.Operations.Where(o => o.PartId == target.Id).OrderBy(o => o.StepNumber).ToListAsync();
        ops.Select(o => o.Title).Should().Equal(sourceOps.Select(o => o.Title));
        ops.Select(o => o.StepNumber).Should().Equal(sourceOps.Select(o => o.StepNumber));
        ops.Select(o => o.EstimatedMs).Should().Equal(sourceOps.Select(o => o.EstimatedMs));
        ops.Select(o => o.SetupMinutes).Should().Equal(sourceOps.Select(o => o.SetupMinutes));
        ops.Select(o => o.RunMinutesLot).Should().Equal(sourceOps.Select(o => o.RunMinutesLot));
        ops[8].ReferencedOperationId.Should().Be(ops[1].Id);
        ops.Select(o => o.Id).Should().NotIntersectWith(sourceOps.Select(o => o.Id));

        (await _db.Operations.CountAsync(o => o.PartId == source.Id)).Should().Be(9);
    }

    [Fact]
    public async Task Copy_LinksMaterialsToTheTargetBomLineForTheSameComponent()
    {
        var (source, bolt, _) = await SeedSourceAsync();
        var target = await SeedPartAsync("ASM-00008");
        var targetLine = new BOMLine { ParentPartId = target.Id, ChildPartId = bolt.Id, Quantity = 6, SortOrder = 1 };
        _db.BOMLines.Add(targetLine);
        await _db.SaveChangesAsync();

        await _handler.Handle(new CopyRoutingCommand(target.Id, source.Id), CancellationToken.None);

        var materials = await _db.OperationMaterials.Include(m => m.Operation)
            .Where(m => m.Operation.PartId == target.Id)
            .ToListAsync();
        var material = materials.Should().ContainSingle().Subject;
        material.BomLineId.Should().Be(targetLine.Id);
        material.Quantity.Should().Be(4);
        material.Notes.Should().Be("Torque to spec");
        material.Operation.StepNumber.Should().Be(30);
    }

    [Fact]
    public async Task Copy_LogsActivityOnTheTargetPart()
    {
        var (source, _, _) = await SeedSourceAsync();
        var target = await SeedPartAsync("ASM-00008");

        await _handler.Handle(new CopyRoutingCommand(target.Id, source.Id), CancellationToken.None);

        var log = await _db.ActivityLogs.SingleAsync(a => a.EntityType == "Part" && a.EntityId == target.Id && a.Action == "routing-copied");
        log.Description.Should().Contain(source.PartNumber).And.Contain("9 operations");
    }

    [Fact]
    public async Task Copy_RefusesATargetThatAlreadyHasOperations()
    {
        var (source, _, _) = await SeedSourceAsync();
        var target = await SeedPartAsync("ASM-00008");
        await _handler.Handle(new CopyRoutingCommand(target.Id, source.Id), CancellationToken.None);

        var act = () => _handler.Handle(new CopyRoutingCommand(target.Id, source.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already has a routing*");
        (await _db.Operations.CountAsync(o => o.PartId == target.Id)).Should().Be(9);
        (await _db.ActivityLogs.CountAsync(a => a.EntityType == "Part" && a.EntityId == target.Id && a.Action == "routing-copied")).Should().Be(1);
    }

    [Fact]
    public async Task Copy_RefusesASourceWithoutOperations()
    {
        var source = await SeedPartAsync("ASM-00001");
        var target = await SeedPartAsync("ASM-00002");

        var act = () => _handler.Handle(new CopyRoutingCommand(target.Id, source.Id), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*no routing*");
    }

    [Fact]
    public async Task Copy_ThrowsNotFound_ForAMissingPart()
    {
        var (source, _, _) = await SeedSourceAsync();

        var act = () => _handler.Handle(new CopyRoutingCommand(9999, source.Id), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public void Validator_RejectsCopyingAPartOntoItself()
    {
        var result = new CopyRoutingCommandValidator().Validate(new CopyRoutingCommand(5, 5));

        result.Errors.Should().ContainSingle(e => e.PropertyName == nameof(CopyRoutingCommand.SourcePartId));
    }
}
