using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Parts;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class ClonePartTests
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly Mock<ISystemSettingRepository> _settings = new();
    private readonly Mock<IBomRevisionService> _bomRevisions = new();
    private readonly Mock<ISender> _sender = new();
    private readonly ClonePartHandler _handler;

    public ClonePartTests()
    {
        var identifiers = new Mock<IBusinessIdentifierService>();
        identifiers.Setup(i => i.IssueAsync(It.IsAny<BusinessEntityType>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new BusinessIdentifier());
        var pricing = new Mock<IPartPricingResolver>();
        pricing.Setup(r => r.ResolveAsync(It.IsAny<int>(), null, null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((int partId, int? _, decimal? _, CancellationToken _) =>
                new ResolvedPartPrice(partId, 0m, "USD", PartPriceSource.Default, null, null));

        _handler = new ClonePartHandler(
            _db,
            new PartRepository(_db, pricing.Object),
            _settings.Object,
            Mock.Of<IBarcodeService>(),
            identifiers.Object,
            _bomRevisions.Object,
            _sender.Object);
    }

    private async Task<Part> SeedPartAsync(string partNumber, InventoryClass inventoryClass = InventoryClass.Subassembly)
    {
        var part = new Part
        {
            PartNumber = partNumber,
            Name = partNumber,
            Description = "Source description",
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = inventoryClass,
            Status = PartStatus.Active,
            HtsCode = "8481.80",
            ExternalId = "QB-42",
            ManualCostOverride = 12.5m,
            SafetyStockQty = 40,
        };
        _db.Parts.Add(part);
        await _db.SaveChangesAsync();
        return part;
    }

    private async Task<(Part Source, List<BOMLine> Bom, List<Operation> Ops)> SeedFamilyMemberAsync()
    {
        var source = await SeedPartAsync("ASM-00007");
        var bolt = await SeedPartAsync("PRT-00001", InventoryClass.Component);
        var plate = await SeedPartAsync("PRT-00002", InventoryClass.Component);

        var bom = new List<BOMLine>
        {
            new() { ParentPartId = source.Id, ChildPartId = bolt.Id, Quantity = 4, SortOrder = 1, ReferenceDesignator = "B1" },
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
                Instructions = $"Do step {step}",
                WorkCenterId = 100 + step,
                SetupMinutes = step,
                RunMinutesEach = step * 2,
                IsQcCheckpoint = step == 9,
                QcCriteria = step == 9 ? "Final inspection" : null,
            })
            .Reverse()
            .ToList();
        _db.Operations.AddRange(ops);
        await _db.SaveChangesAsync();

        var ordered = ops.OrderBy(o => o.StepNumber).ToList();
        ordered[8].ReferencedOperationId = ordered[1].Id;
        _db.OperationMaterials.Add(new OperationMaterial { OperationId = ordered[2].Id, BomLineId = bom[0].Id, Quantity = 4, Notes = "Torque to spec" });
        await _db.SaveChangesAsync();

        return (source, bom, ordered);
    }

    private static ClonePartRequestModel Request(
        string? partNumber = null, bool copyBom = true, bool copyRouting = true, bool copyVendorSources = false)
        => new("Valve body, 3/4 in", partNumber, null, copyBom, copyRouting, copyVendorSources);

    private void EnableManualNumbers()
        => _settings.Setup(s => s.FindByKeyAsync("parts.allow_manual_numbers", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "parts.allow_manual_numbers", Value = "true" });

    [Fact]
    public async Task Clone_CopiesRoutingInStepOrder_WithRemappedReferencesAndMaterials()
    {
        var (source, _, sourceOps) = await SeedFamilyMemberAsync();

        var result = await _handler.Handle(new ClonePartCommand(source.Id, Request()), CancellationToken.None);

        result.Id.Should().NotBe(source.Id);
        result.PartNumber.Should().Be("ASM-00008");
        result.Status.Should().Be(PartStatus.Draft);
        result.Name.Should().Be("Valve body, 3/4 in");
        result.Description.Should().Be("Source description");

        var ops = await _db.Operations.Include(o => o.Materials).ThenInclude(m => m.BomLine)
            .Where(o => o.PartId == result.Id)
            .OrderBy(o => o.StepNumber)
            .ToListAsync();
        ops.Should().HaveCount(9);
        ops.Select(o => o.Title).Should().Equal(sourceOps.Select(o => o.Title));
        ops.Select(o => o.WorkCenterId).Should().Equal(sourceOps.Select(o => o.WorkCenterId));
        ops[8].IsQcCheckpoint.Should().BeTrue();
        ops[8].QcCriteria.Should().Be("Final inspection");
        ops[8].ReferencedOperationId.Should().Be(ops[1].Id);
        ops.Select(o => o.Id).Should().NotIntersectWith(sourceOps.Select(o => o.Id));

        var material = ops[2].Materials.Should().ContainSingle().Subject;
        material.Quantity.Should().Be(4);
        material.Notes.Should().Be("Torque to spec");
        material.BomLine.ParentPartId.Should().Be(result.Id);
        material.BomLine.ReferenceDesignator.Should().Be("B1");

        _sender.Verify(s => s.Send(
            It.Is<RecalculatePartStandardCostCommand>(c => c.PartId == result.Id), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clone_CopiesBom_AndCapturesARevision()
    {
        var (source, sourceBom, _) = await SeedFamilyMemberAsync();

        var result = await _handler.Handle(
            new ClonePartCommand(source.Id, Request(copyRouting: false)), CancellationToken.None);

        var bom = await _db.BOMLines.Where(b => b.ParentPartId == result.Id).OrderBy(b => b.SortOrder).ToListAsync();
        bom.Select(b => (b.ChildPartId, b.Quantity, b.SortOrder))
            .Should().Equal(sourceBom.Select(b => (b.ChildPartId, b.Quantity, b.SortOrder)));
        (await _db.Operations.CountAsync(o => o.PartId == result.Id)).Should().Be(0);
        (await _db.BOMLines.CountAsync(b => b.ParentPartId == source.Id)).Should().Be(2);

        _bomRevisions.Verify(r => r.CaptureCurrentStateAsync(
            result.Id, It.IsAny<int?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Clone_WithoutBom_DropsOperationMaterials()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();

        var result = await _handler.Handle(
            new ClonePartCommand(source.Id, Request(copyBom: false)), CancellationToken.None);

        (await _db.Operations.CountAsync(o => o.PartId == result.Id)).Should().Be(9);
        (await _db.BOMLines.CountAsync(b => b.ParentPartId == result.Id)).Should().Be(0);
        (await _db.OperationMaterials.CountAsync(m => m.Operation.PartId == result.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Clone_NullsReferenceToAnOperationOutsideTheRouting()
    {
        var source = await SeedPartAsync("ASM-00001");
        var other = await SeedPartAsync("ASM-00002");
        var foreign = new Operation { PartId = other.Id, StepNumber = 10, Title = "Elsewhere" };
        _db.Operations.Add(foreign);
        await _db.SaveChangesAsync();
        _db.Operations.Add(new Operation { PartId = source.Id, StepNumber = 10, Title = "Linked", ReferencedOperationId = foreign.Id });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ClonePartCommand(source.Id, Request()), CancellationToken.None);

        var copied = await _db.Operations.SingleAsync(o => o.PartId == result.Id);
        copied.ReferencedOperationId.Should().BeNull();
    }

    [Fact]
    public async Task Clone_SkipsVendorSources_WhenNotRequested()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();
        _db.VendorParts.Add(new VendorPart { PartId = source.Id, VendorId = 5, VendorPartNumber = "V-1", IsPreferred = true });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(new ClonePartCommand(source.Id, Request()), CancellationToken.None);

        (await _db.VendorParts.CountAsync(v => v.PartId == result.Id)).Should().Be(0);
    }

    [Fact]
    public async Task Clone_CopiesVendorSources_WithPreferenceCleared()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();
        _db.VendorParts.Add(new VendorPart { PartId = source.Id, VendorId = 5, VendorPartNumber = "V-1", LeadTimeDays = 14, IsPreferred = true });
        await _db.SaveChangesAsync();

        var result = await _handler.Handle(
            new ClonePartCommand(source.Id, Request(copyVendorSources: true)), CancellationToken.None);

        var copied = await _db.VendorParts.SingleAsync(v => v.PartId == result.Id);
        copied.VendorId.Should().Be(5);
        copied.VendorPartNumber.Should().Be("V-1");
        copied.LeadTimeDays.Should().Be(14);
        copied.IsPreferred.Should().BeFalse();
    }

    [Fact]
    public async Task Clone_DoesNotCarryAccountingLinksCostOverrideOrStockPolicy()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();

        var result = await _handler.Handle(new ClonePartCommand(source.Id, Request()), CancellationToken.None);

        var clone = await _db.Parts.SingleAsync(p => p.Id == result.Id);
        clone.HtsCode.Should().Be("8481.80");
        clone.ExternalId.Should().BeNull();
        clone.ManualCostOverride.Should().BeNull();
        clone.SafetyStockQty.Should().Be(0);
        clone.Revision.Should().Be("A");
    }

    [Fact]
    public async Task Clone_LogsActivityOnTheNewPart()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();

        var result = await _handler.Handle(new ClonePartCommand(source.Id, Request()), CancellationToken.None);

        var log = await _db.ActivityLogs.SingleAsync(a => a.EntityType == "Part" && a.EntityId == result.Id && a.Action == "created");
        log.Description.Should().Contain(source.PartNumber).And.Contain("9 operations");
    }

    [Fact]
    public async Task Clone_UsesSuppliedNumber_WhenManualNumbersEnabled()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();
        EnableManualNumbers();

        var result = await _handler.Handle(
            new ClonePartCommand(source.Id, Request(partNumber: " VB-075 ")), CancellationToken.None);

        result.PartNumber.Should().Be("VB-075");
    }

    [Fact]
    public async Task Clone_RejectsDuplicatePartNumber_IncludingSoftDeletedParts()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();
        var retired = await SeedPartAsync("VB-050");
        retired.DeletedAt = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync();
        EnableManualNumbers();

        var act = () => _handler.Handle(
            new ClonePartCommand(source.Id, Request(partNumber: "VB-050")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already in use*");
        (await _db.Parts.IgnoreQueryFilters().CountAsync()).Should().Be(4);
    }

    [Fact]
    public async Task Clone_RejectsSuppliedNumber_WhenManualNumbersDisabled()
    {
        var (source, _, _) = await SeedFamilyMemberAsync();

        var act = () => _handler.Handle(
            new ClonePartCommand(source.Id, Request(partNumber: "VB-075")), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*not enabled*");
    }

    [Fact]
    public async Task Clone_ThrowsNotFound_ForMissingSource()
    {
        var act = () => _handler.Handle(new ClonePartCommand(999, Request()), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }
}
