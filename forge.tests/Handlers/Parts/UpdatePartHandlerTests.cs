using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Forge.Api.Features.Parts;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Handlers.Parts;

public class UpdatePartHandlerTests
{
    private readonly Mock<IPartRepository> _partRepo = new();
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private readonly UpdatePartHandler _handler;

    public UpdatePartHandlerTests()
    {
        _handler = new UpdatePartHandler(
            _partRepo.Object,
            Mock.Of<ISyncQueueRepository>(),
            Mock.Of<IAccountingProviderFactory>(),
            _db,
            Mock.Of<ILogger<UpdatePartHandler>>());
    }

    private static PartDetailResponseModel BuildDetailResponse() =>
        new(
            Id: 1,
            PartNumber: "PRT-00001",
            Name: "Test",
            Description: null,
            Revision: "A",
            Status: PartStatus.Active,
            ProcurementSource: ProcurementSource.Buy,
            InventoryClass: InventoryClass.Component,
            ItemKindId: null,
            ItemKindLabel: null,
            TraceabilityType: TraceabilityType.None,
            AbcClass: null,
            MaterialSpecId: null,
            MaterialSpecLabel: null,
            ExternalId: null,
            ExternalRef: null,
            Provider: null,
            PreferredVendorId: null,
            PreferredVendorName: null,
            MinStockThreshold: null,
            ReorderPoint: null,
            ReorderQuantity: null,
            SafetyStockDays: null,
            ToolingAssetId: null,
            ToolingAssetName: null,
            ManualCostOverride: null,
            CurrentCostCalculationId: null,
            WeightEach: null,
            WeightDisplayUnit: null,
            LengthMm: null,
            WidthMm: null,
            HeightMm: null,
            DimensionDisplayUnit: null,
            VolumeMl: null,
            VolumeDisplayUnit: null,
            ValuationClassId: null,
            ValuationClassLabel: null,
            HtsCode: null,
            HazmatClass: null,
            ShelfLifeDays: null,
            BackflushPolicy: null,
            IsKit: false,
            IsConfigurable: false,
            DefaultBinId: null,
            SourcePartId: null,
            BomEntries: new List<BOMEntryResponseModel>(),
            UsedIn: new List<BOMUsageResponseModel>(),
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: DateTime.UtcNow,
            StockUomId: null,
            StockUomCode: null,
            StockUomLabel: null,
            PurchaseUomId: null,
            PurchaseUomCode: null,
            PurchaseUomLabel: null,
            SalesUomId: null,
            SalesUomCode: null,
            SalesUomLabel: null,
            IsMrpPlanned: false,
            LotSizingRule: null,
            FixedOrderQuantity: null,
            MinimumOrderQuantity: null,
            OrderMultiple: null,
            PlanningFenceDays: null,
            DemandFenceDays: null,
            RequiresReceivingInspection: false,
            ReceivingInspectionTemplateId: null,
            InspectionFrequency: null,
            InspectionSkipAfterN: null);

    private void SetupRepoForUpdate(Part part)
    {
        _partRepo.Setup(r => r.FindAsync(part.Id, It.IsAny<CancellationToken>())).ReturnsAsync(part);
        _partRepo.Setup(r => r.SaveChangesAsync(It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        _partRepo.Setup(r => r.GetDetailAsync(part.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(BuildDetailResponse());
    }

    private static UpdatePartRequestModel EmptyUpdate() => new(
        Name: null, Description: null, Revision: null, Status: null,
        ProcurementSource: null, InventoryClass: null,
        ToolingAssetId: null, PreferredVendorId: null,
        MinStockThreshold: null, ReorderPoint: null, ReorderQuantity: null,
        SafetyStockDays: null);

    [Fact]
    public async Task Handle_SetsMrpFields_PersistsThem()
    {
        // Arrange
        var part = new Part { Id = 1, PartNumber = "PRT-00001", Name = "Test", Status = PartStatus.Active };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            IsMrpPlanned = true,
            LotSizingRule = LotSizingRule.FixedQuantity,
            FixedOrderQuantity = 100m,
            PlanningFenceDays = 14,
            DemandFenceDays = 7,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.IsMrpPlanned.Should().BeTrue();
        part.LotSizingRule.Should().Be(LotSizingRule.FixedQuantity);
        part.FixedOrderQuantity.Should().Be(100m);
        part.PlanningFenceDays.Should().Be(14);
        part.DemandFenceDays.Should().Be(7);
    }

    [Fact]
    public async Task Handle_SetsUomIds_PersistsThem()
    {
        // Arrange
        var part = new Part { Id = 1, PartNumber = "PRT-00001", Name = "Test", Status = PartStatus.Active };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            StockUomId = 5,
            PurchaseUomId = 6,
            SalesUomId = 7,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.StockUomId.Should().Be(5);
        part.PurchaseUomId.Should().Be(6);
        part.SalesUomId.Should().Be(7);
    }

    [Fact]
    public async Task Handle_SetsMaterialClusterFields_PersistsThem()
    {
        // Arrange
        var part = new Part { Id = 1, PartNumber = "PRT-00001", Name = "Test", Status = PartStatus.Active };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            MaterialSpecId = 42,
            WeightEach = 1500.5m,
            WeightDisplayUnit = "kg",
            LengthMm = 100m,
            WidthMm = 50m,
            HeightMm = 25m,
            DimensionDisplayUnit = "mm",
            VolumeMl = 250m,
            VolumeDisplayUnit = "mL",
            ValuationClassId = 3,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.MaterialSpecId.Should().Be(42);
        part.WeightEach.Should().Be(1500.5m);
        part.WeightDisplayUnit.Should().Be("kg");
        part.LengthMm.Should().Be(100m);
        part.WidthMm.Should().Be(50m);
        part.HeightMm.Should().Be(25m);
        part.DimensionDisplayUnit.Should().Be("mm");
        part.VolumeMl.Should().Be(250m);
        part.VolumeDisplayUnit.Should().Be("mL");
        part.ValuationClassId.Should().Be(3);
    }

    [Fact]
    public async Task Handle_SetsQualityClusterFields_PersistsThem()
    {
        // Arrange
        var part = new Part { Id = 1, PartNumber = "PRT-00001", Name = "Test", Status = PartStatus.Active };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            RequiresReceivingInspection = true,
            ReceivingInspectionTemplateId = 9,
            InspectionFrequency = ReceivingInspectionFrequency.SkipLot,
            InspectionSkipAfterN = 5,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.RequiresReceivingInspection.Should().BeTrue();
        part.ReceivingInspectionTemplateId.Should().Be(9);
        part.InspectionFrequency.Should().Be(ReceivingInspectionFrequency.SkipLot);
        part.InspectionSkipAfterN.Should().Be(5);
    }

    [Fact]
    public async Task Handle_NegativeOneSentinel_ClearsFkToNull()
    {
        // Arrange — pre-populate FK fields, then clear via -1.
        var part = new Part
        {
            Id = 1,
            PartNumber = "PRT-00001",
            Name = "Test",
            Status = PartStatus.Active,
            MaterialSpecId = 42,
            ValuationClassId = 3,
            StockUomId = 5,
            DefaultBinId = 99,
            SourcePartId = 7,
            ReceivingInspectionTemplateId = 9,
        };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            StockUomId = -1,
            ReceivingInspectionTemplateId = -1,
            MaterialSpecId = -1,
            ValuationClassId = -1,
            DefaultBinId = -1,
            SourcePartId = -1,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.MaterialSpecId.Should().BeNull();
        part.ValuationClassId.Should().BeNull();
        part.StockUomId.Should().BeNull();
        part.DefaultBinId.Should().BeNull();
        part.SourcePartId.Should().BeNull();
        part.ReceivingInspectionTemplateId.Should().BeNull();
    }

    [Fact]
    public async Task Handle_NullableBool_NullMeansNoChange()
    {
        // Arrange — pre-set values; null IsKit/IsConfigurable in request must NOT mutate.
        var part = new Part
        {
            Id = 1,
            PartNumber = "PRT-00001",
            Name = "Test",
            Status = PartStatus.Active,
            IsMrpPlanned = true,
            IsKit = true,
            IsConfigurable = true,
            RequiresReceivingInspection = true,
        };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            IsMrpPlanned = null,
            IsKit = null,
            IsConfigurable = null,
            RequiresReceivingInspection = null,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert — none of these should have flipped because the request was null.
        part.IsMrpPlanned.Should().BeTrue();
        part.IsKit.Should().BeTrue();
        part.IsConfigurable.Should().BeTrue();
        part.RequiresReceivingInspection.Should().BeTrue();
    }

    [Fact]
    public async Task Handle_NullableBool_ExplicitFalseSetsFalse()
    {
        // Arrange — flipping explicit bool? false must clear flags.
        var part = new Part
        {
            Id = 1,
            PartNumber = "PRT-00001",
            Name = "Test",
            Status = PartStatus.Active,
            IsMrpPlanned = true,
            IsKit = true,
            IsConfigurable = true,
            RequiresReceivingInspection = true,
        };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            IsMrpPlanned = false,
            IsKit = false,
            IsConfigurable = false,
            RequiresReceivingInspection = false,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.IsMrpPlanned.Should().BeFalse();
        part.IsKit.Should().BeFalse();
        part.IsConfigurable.Should().BeFalse();
        part.RequiresReceivingInspection.Should().BeFalse();
    }

    [Fact]
    public async Task Handle_HtsCodeAndHazmatTrim_EmptyClearsToNull()
    {
        // Arrange
        var part = new Part
        {
            Id = 1,
            PartNumber = "PRT-00001",
            Name = "Test",
            Status = PartStatus.Active,
            HtsCode = "1234567890",
            HazmatClass = "Class 3",
        };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            HazmatClass = "  ",
            HtsCode = "",
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.HtsCode.Should().BeNull();
        part.HazmatClass.Should().BeNull();
    }

    [Fact]
    public async Task Handle_AxisChange_PersistsThem()
    {
        // Arrange — Pre-beta: the three orthogonal axes are the only sourcing
        // identity. Updating procurement + inventory class swaps them on the row.
        var part = new Part
        {
            Id = 1,
            PartNumber = "PRT-00001",
            Name = "Test",
            Status = PartStatus.Active,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
        };
        SetupRepoForUpdate(part);

        var req = EmptyUpdate() with
        {
            ProcurementSource = ProcurementSource.Make,
            InventoryClass = InventoryClass.Subassembly,
        };

        // Act
        await _handler.Handle(new UpdatePartCommand(1, req), CancellationToken.None);

        // Assert
        part.ProcurementSource.Should().Be(ProcurementSource.Make);
        part.InventoryClass.Should().Be(InventoryClass.Subassembly);
    }
}
