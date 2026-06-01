using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Moq;

using Forge.Api.Features.AutoPo;
using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

/// <summary>
/// Pillar 3 — proves the AutoPurchaseOrderJob honors per-vendor PackSize /
/// MinOrderQty from the preferred VendorPart row via IPartSourcingResolver.
/// Vendor-specific terms live exclusively on VendorPart now (the legacy
/// Part snapshot columns were dropped post-OEM-on-VendorPart move) — when
/// no preferred VendorPart exists the job uses the raw shortfall without
/// rounding to pack size or applying a minimum.
///
/// We exercise the Suggest mode path so the test does not need to wire
/// up <see cref="PurchaseOrderGenerator"/> + IPurchaseOrderRepository +
/// IBarcodeService. Suggest mode writes <see cref="AutoPoSuggestion"/>
/// rows whose <c>SuggestedQty</c> reflects the resolved sourcing values.
/// </summary>
public class AutoPurchaseOrderJobMigrationTests
{
    private static async Task<(int VendorId, int ParentId, int ChildId)> SeedScenarioAsync(
        AppDbContext db,
        decimal soQty,
        decimal bomQuantityPerUnit,
        decimal? vendorPartPackSize = null,
        decimal? vendorPartMinOrderQty = null,
        bool createPreferredVendorPart = false)
    {
        var vendor = new Vendor
        {
            CompanyName = "AutoPO Vendor",
            AutoPoMode = AutoPoMode.Suggest,
        };
        db.Vendors.Add(vendor);

        var parent = new Part
        {
            PartNumber = "PARENT-001", Name = "Parent",
            ProcurementSource = ProcurementSource.Buy, InventoryClass = InventoryClass.Component, Status = PartStatus.Active,
        };
        var child = new Part
        {
            PartNumber = "CHILD-001", Name = "Child",
            ProcurementSource = ProcurementSource.Buy, InventoryClass = InventoryClass.Component, Status = PartStatus.Active,
            PreferredVendorId = null,
        };
        db.Parts.AddRange(parent, child);
        await db.SaveChangesAsync();

        // Wire the child's preferred vendor to our test vendor (used by
        // the job to pick the vendor for the suggestion).
        child.PreferredVendorId = vendor.Id;
        await db.SaveChangesAsync();

        if (createPreferredVendorPart)
        {
            db.VendorParts.Add(new VendorPart
            {
                VendorId = vendor.Id,
                PartId = child.Id,
                IsPreferred = true,
                PackSize = vendorPartPackSize,
                MinOrderQty = vendorPartMinOrderQty,
            });
            await db.SaveChangesAsync();
        }

        // BOM: parent consumes child at the configured ratio.
        db.BOMLines.Add(new BOMLine
        {
            ParentPartId = parent.Id,
            ChildPartId = child.Id,
            Quantity = bomQuantityPerUnit,
            SourceType = BOMSourceType.Buy,
        });

        // Customer + open SO + line so demand is non-zero.
        var customer = new Customer { Name = "Test Customer" };
        db.Customers.Add(customer);
        await db.SaveChangesAsync();

        var so = new SalesOrder
        {
            OrderNumber = "SO-AUTO-001",
            CustomerId = customer.Id,
            Status = SalesOrderStatus.Confirmed,
            RequestedDeliveryDate = DateTimeOffset.UtcNow.AddDays(30),
        };
        db.SalesOrders.Add(so);
        await db.SaveChangesAsync();

        db.SalesOrderLines.Add(new SalesOrderLine
        {
            SalesOrderId = so.Id,
            PartId = parent.Id,
            Description = "Parent",
            Quantity = soQty,
            UnitPrice = 1m,
        });

        // Auto-PO master switch ON, mode Suggest.
        db.SystemSettings.AddRange(
            new SystemSetting { Key = "inventory:auto_po_enabled", Value = "true" },
            new SystemSetting { Key = "inventory:auto_po_mode", Value = "Suggest" });

        await db.SaveChangesAsync();
        return (vendor.Id, parent.Id, child.Id);
    }

    private static AutoPurchaseOrderJob BuildJob(AppDbContext db)
    {
        var settingsRepo = new Mock<ISystemSettingRepository>();
        settingsRepo.Setup(r => r.FindByKeyAsync("inventory:auto_po_enabled", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "inventory:auto_po_enabled", Value = "true" });
        settingsRepo.Setup(r => r.FindByKeyAsync("inventory:auto_po_mode", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "inventory:auto_po_mode", Value = "Suggest" });
        settingsRepo.Setup(r => r.FindByKeyAsync("inventory:auto_po_buffer_days", It.IsAny<CancellationToken>()))
            .ReturnsAsync(new SystemSetting { Key = "inventory:auto_po_buffer_days", Value = "3" });

        // PurchaseOrderGenerator is concrete — but Suggest mode never calls
        // it. Constructing one with mock collaborators is acceptable.
        var poGen = new PurchaseOrderGenerator(
            db,
            Mock.Of<IPurchaseOrderRepository>(),
            Mock.Of<IBarcodeService>());

        return new AutoPurchaseOrderJob(
            db,
            new SystemClock(),
            settingsRepo.Object,
            poGen,
            new PartSourcingResolver(db),
            NullLogger<AutoPurchaseOrderJob>.Instance);
    }

    [Fact]
    public async Task Execute_NoPreferredVendorPart_UsesRawShortfallWithoutRoundingOrMinimum()
    {
        // Arrange — no VendorPart row at all. SO of 13 parents × 1 BOM qty
        // = raw shortfall 13. With no per-vendor pack size or min order qty
        // resolved, the job records the shortfall as-is.
        using var db = TestDbContextFactory.Create();
        var (_, _, childId) = await SeedScenarioAsync(
            db,
            soQty: 13m,
            bomQuantityPerUnit: 1m,
            createPreferredVendorPart: false);

        var job = BuildJob(db);

        // Act
        await job.Execute(CancellationToken.None);

        // Assert
        var suggestion = db.AutoPoSuggestions.SingleOrDefault(s => s.PartId == childId);
        suggestion.Should().NotBeNull();
        suggestion!.SuggestedQty.Should().Be(13m);
    }

    [Fact]
    public async Task Execute_PreferredVendorPart_AppliesItsPackSizeAndMinOrderQty()
    {
        // Arrange — preferred VendorPart with PackSize=4 / MinOrderQty=8.
        // SO of 13 parents × 1 BOM qty = shortfall 13 → rounded up to next
        // multiple of 4 = 16. Min (8) is below 16, no bump.
        using var db = TestDbContextFactory.Create();
        var (_, _, childId) = await SeedScenarioAsync(
            db,
            soQty: 13m,
            bomQuantityPerUnit: 1m,
            vendorPartPackSize: 4m,
            vendorPartMinOrderQty: 8m,
            createPreferredVendorPart: true);

        var job = BuildJob(db);

        // Act
        await job.Execute(CancellationToken.None);

        // Assert
        var suggestion = db.AutoPoSuggestions.SingleOrDefault(s => s.PartId == childId);
        suggestion.Should().NotBeNull();
        suggestion!.SuggestedQty.Should().Be(16m);
    }
}
