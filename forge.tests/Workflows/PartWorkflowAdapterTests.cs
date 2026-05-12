using System.Text.Json;

using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

using Forge.Api.Workflows;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Data.Context;
using Forge.Tests.Capabilities;

namespace Forge.Tests.Workflows;

/// <summary>
/// Pillar 6 follow-up — verifies <see cref="PartWorkflowAdapter.ApplyAsync"/>
/// recognizes every field that the new step components (PartSourcingStep,
/// PartInventoryStep, PartQualityStep, PartToolAssetStep, etc. shipped in
/// b8ef771) emit. Each test exercises one cluster-group; together they
/// confirm the adapter no longer silently drops step-component fields.
/// </summary>
[Collection(CapabilityTestCollection.Name)]
public class PartWorkflowAdapterTests(CapabilityTestWebApplicationFactory factory)
{
    /// <summary>
    /// Helper: materializes a draft Part directly via the repo so we can
    /// poke ApplyAsync without the workflow envelope. The adapter only
    /// touches the row pointed at by entityId; we don't need the full
    /// workflow run for these focused tests.
    /// </summary>
    private async Task<int> CreateDraftPartAsync()
    {
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = new Part
        {
            PartNumber = $"WA-{Guid.NewGuid():N}"[..16],
            Name = "Adapter Test",
            Status = PartStatus.Draft,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
        };
        db.Parts.Add(part);
        await db.SaveChangesAsync();
        return part.Id;
    }

    private static PartWorkflowAdapter ResolveAdapter(IServiceScope scope)
    {
        // The adapter is registered both as its concrete type and behind the
        // three IWorkflow* interfaces. Resolve the concrete type directly so
        // we get a single instance scoped to this DI scope's AppDbContext.
        return scope.ServiceProvider.GetRequiredService<PartWorkflowAdapter>();
    }

    [Fact]
    public async Task ApplyAsync_SourcingCluster_PersistsPreferredVendorIdOnly()
    {
        // The Sourcing step's cluster payload is allowed to carry per-vendor
        // values for back-compat with older clients, but the only field that
        // lands on Part is preferredVendorId — lead time / MOQ / pack size
        // are vendor-specific and live on the VendorPart row now.
        var partId = await CreateDraftPartAsync();

        int vendorId;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var v = new Vendor { CompanyName = "Acme Distributors" };
            db.Vendors.Add(v);
            await db.SaveChangesAsync();
            vendorId = v.Id;
        }

        var fields = JsonDocument.Parse($$"""
        {
          "preferredVendorId": {{vendorId}},
          "leadTimeDays": 14,
          "minOrderQty": 25,
          "packSize": 10
        }
        """).RootElement;

        using (var scope = factory.Services.CreateScope())
        {
            var adapter = ResolveAdapter(scope);
            await adapter.ApplyAsync(partId, fields, default);
        }

        using var verify = factory.Services.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = await ctx.Parts.AsNoTracking().FirstAsync(p => p.Id == partId);
        part.PreferredVendorId.Should().Be(vendorId);

        // No VendorPart row was created — vendor-specific terms must reach
        // the part through the Vendor Parts workflow step, not Sourcing.
        var hasVendorPartRow = await ctx.VendorParts.AnyAsync(vp => vp.PartId == partId);
        hasVendorPartRow.Should().BeFalse();
    }

    [Fact]
    public async Task ApplyAsync_PersistsInventoryAndUomClusterFields()
    {
        var partId = await CreateDraftPartAsync();

        // Seed three UoM rows so the FKs are meaningful.
        int eachId, kgId, boxId;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var ea = new UnitOfMeasure { Code = "ea", Name = "each", IsActive = true };
            var kg = new UnitOfMeasure { Code = "kg", Name = "kilogram", IsActive = true };
            var box = new UnitOfMeasure { Code = "box", Name = "box", IsActive = true };
            db.UnitsOfMeasure.AddRange(ea, kg, box);
            await db.SaveChangesAsync();
            eachId = ea.Id;
            kgId = kg.Id;
            boxId = box.Id;
        }

        var fields = JsonDocument.Parse($$"""
        {
          "minStockThreshold": 5,
          "reorderPoint": 10,
          "reorderQuantity": 50,
          "safetyStockDays": 7,
          "stockUomId": {{eachId}},
          "purchaseUomId": {{kgId}},
          "salesUomId": {{boxId}}
        }
        """).RootElement;

        using (var scope = factory.Services.CreateScope())
        {
            var adapter = ResolveAdapter(scope);
            await adapter.ApplyAsync(partId, fields, default);
        }

        using var verify = factory.Services.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = await ctx.Parts.AsNoTracking().FirstAsync(p => p.Id == partId);
        part.MinStockThreshold.Should().Be(5m);
        part.ReorderPoint.Should().Be(10m);
        part.ReorderQuantity.Should().Be(50m);
        part.SafetyStockDays.Should().Be(7);
        part.StockUomId.Should().Be(eachId);
        part.PurchaseUomId.Should().Be(kgId);
        part.SalesUomId.Should().Be(boxId);
    }

    [Fact]
    public async Task ApplyAsync_PersistsQualityClusterFields()
    {
        var partId = await CreateDraftPartAsync();

        var fields = JsonDocument.Parse("""
        {
          "requiresReceivingInspection": true,
          "inspectionFrequency": "SkipLot",
          "inspectionSkipAfterN": 5
        }
        """).RootElement;

        using (var scope = factory.Services.CreateScope())
        {
            var adapter = ResolveAdapter(scope);
            await adapter.ApplyAsync(partId, fields, default);
        }

        using var verify = factory.Services.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = await ctx.Parts.AsNoTracking().FirstAsync(p => p.Id == partId);
        part.RequiresReceivingInspection.Should().BeTrue();
        part.InspectionFrequency.Should().Be(ReceivingInspectionFrequency.SkipLot);
        part.InspectionSkipAfterN.Should().Be(5);
    }

    [Fact]
    public async Task ApplyAsync_PersistsToolingClusterField()
    {
        var partId = await CreateDraftPartAsync();

        // Seed an Asset row so ToolingAssetId is meaningful.
        int assetId;
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var a = new Asset { Name = "Mold #14", AssetType = AssetType.Tooling };
            db.Assets.Add(a);
            await db.SaveChangesAsync();
            assetId = a.Id;
        }

        var fields = JsonDocument.Parse($$"""
        {
          "toolingAssetId": {{assetId}}
        }
        """).RootElement;

        using (var scope = factory.Services.CreateScope())
        {
            var adapter = ResolveAdapter(scope);
            await adapter.ApplyAsync(partId, fields, default);
        }

        using var verify = factory.Services.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = await ctx.Parts.AsNoTracking().FirstAsync(p => p.Id == partId);
        part.ToolingAssetId.Should().Be(assetId);
    }

    [Fact]
    public async Task ApplyAsync_PersistsMrpClusterFields()
    {
        var partId = await CreateDraftPartAsync();

        var fields = JsonDocument.Parse("""
        {
          "isMrpPlanned": true,
          "lotSizingRule": "FixedQuantity",
          "fixedOrderQuantity": 100,
          "minimumOrderQuantity": 10,
          "orderMultiple": 5,
          "planningFenceDays": 30,
          "demandFenceDays": 7
        }
        """).RootElement;

        using (var scope = factory.Services.CreateScope())
        {
            var adapter = ResolveAdapter(scope);
            await adapter.ApplyAsync(partId, fields, default);
        }

        using var verify = factory.Services.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = await ctx.Parts.AsNoTracking().FirstAsync(p => p.Id == partId);
        part.IsMrpPlanned.Should().BeTrue();
        part.LotSizingRule.Should().Be(LotSizingRule.FixedQuantity);
        part.FixedOrderQuantity.Should().Be(100m);
        part.MinimumOrderQuantity.Should().Be(10m);
        part.OrderMultiple.Should().Be(5m);
        part.PlanningFenceDays.Should().Be(30);
        part.DemandFenceDays.Should().Be(7);
    }

    [Fact]
    public async Task ApplyAsync_NullClearsNullableScalarsInClusterFields()
    {
        // Seed values, then clear via explicit JSON null. Confirms TryRead*
        // helpers honor null-clears across the new cluster fields.
        var partId = await CreateDraftPartAsync();
        using (var seed = factory.Services.CreateScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<AppDbContext>();
            var p = await db.Parts.FirstAsync(x => x.Id == partId);
            p.MinStockThreshold = 42m;
            p.ToolingAssetId = null; // already null but keep for symmetry
            p.PlanningFenceDays = 14;
            await db.SaveChangesAsync();
        }

        var fields = JsonDocument.Parse("""
        {
          "minStockThreshold": null,
          "planningFenceDays": null
        }
        """).RootElement;

        using (var scope = factory.Services.CreateScope())
        {
            var adapter = ResolveAdapter(scope);
            await adapter.ApplyAsync(partId, fields, default);
        }

        using var verify = factory.Services.CreateScope();
        var ctx = verify.ServiceProvider.GetRequiredService<AppDbContext>();
        var part = await ctx.Parts.AsNoTracking().FirstAsync(p => p.Id == partId);
        part.MinStockThreshold.Should().BeNull();
        part.PlanningFenceDays.Should().BeNull();
    }
}
