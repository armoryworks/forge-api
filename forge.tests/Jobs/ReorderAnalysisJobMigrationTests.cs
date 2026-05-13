using FluentAssertions;

using Microsoft.Extensions.Logging.Abstractions;

using Forge.Api.Jobs;
using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Jobs;

/// <summary>
/// Pillar 3 — proves the ReorderAnalysisJob reads lead time from the
/// preferred VendorPart row via IPartSourcingResolver. Vendor-specific
/// terms live exclusively on VendorPart now (the legacy Part snapshot
/// columns were dropped post-OEM-on-VendorPart move) — when no preferred
/// VendorPart is configured the job falls back to a default 14-day lead
/// time inside its cover-window math.
/// </summary>
public class ReorderAnalysisJobMigrationTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 4, 30, 12, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : Forge.Platform.Time.IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    private static Part NewPart(
        string partNumber,
        int? safetyStockDays = null,
        decimal? minStockThreshold = null) => new()
        {
            PartNumber = partNumber,
            Name = partNumber,
            ProcurementSource = ProcurementSource.Buy,
            InventoryClass = InventoryClass.Component,
            Status = PartStatus.Active,
            SafetyStockDays = safetyStockDays,
            MinStockThreshold = minStockThreshold,
        };

    private static async Task SeedConsumption(
        Data.Context.AppDbContext db, int partId, decimal qtyPerDay, int days)
    {
        // Seed BinMovements within 90-day window
        for (var d = 1; d <= days; d++)
        {
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = partId,
                Quantity = qtyPerDay,
                Reason = BinMovementReason.Pick,
                MovedAt = FixedNow.AddDays(-d),
            });
        }
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task RunAnalysis_NoPreferredVendorPart_FallsBackToDefaultLeadTime()
    {
        // Arrange — no VendorPart at all. Job's NeedsReorder defaults the
        // unresolved lead time to 14 days. With burn rate 5/day and safety
        // stock 7 days, cover threshold = (14+7)*5 = 105 units. We seed
        // zero stock so a suggestion is unconditionally created.
        using var db = TestDbContextFactory.Create();
        var part = NewPart("REORDER-NO-VP", safetyStockDays: 7);
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        await SeedConsumption(db, part.Id, qtyPerDay: 5m, days: 90);

        var job = new ReorderAnalysisJob(
            db, new FixedClock(), new PartSourcingResolver(db),
            NullLogger<ReorderAnalysisJob>.Instance);

        // Act
        await job.RunAnalysisAsync();

        // Assert — proves the resolver is called and returns null without
        // crashing the job; the default 14-day fallback drives the reorder.
        db.ReorderSuggestions.Should().Contain(s => s.PartId == part.Id);
    }

    [Fact]
    public async Task RunAnalysis_PreferredVendorPartLeadTime_DrivesCoverThreshold()
    {
        // Arrange — preferred VendorPart with LeadTimeDays=1. With burn
        // rate 5/day and safety stock 7 days, cover threshold drops to
        // (1+7)*5=40 units. We seed 100 units of stock — plenty above the
        // threshold, so NO suggestion is created. (With the default 14-day
        // fallback the threshold would be 105 and a suggestion would fire.)
        using var db = TestDbContextFactory.Create();
        var vendor = new Vendor { CompanyName = "Fast Vendor" };
        db.Vendors.Add(vendor);
        var part = NewPart("REORDER-FAST-VP", safetyStockDays: 7);
        db.Parts.Add(part);
        await db.SaveChangesAsync();

        db.VendorParts.Add(new VendorPart
        {
            VendorId = vendor.Id,
            PartId = part.Id,
            IsPreferred = true,
            LeadTimeDays = 1,
        });
        await db.SaveChangesAsync();

        await SeedConsumption(db, part.Id, qtyPerDay: 5m, days: 90);

        db.BinContents.Add(new BinContent
        {
            EntityType = "part",
            EntityId = part.Id,
            Quantity = 100m,
            ReservedQuantity = 0m,
            LocationId = 1,
        });
        await db.SaveChangesAsync();

        var job = new ReorderAnalysisJob(
            db, new FixedClock(), new PartSourcingResolver(db),
            NullLogger<ReorderAnalysisJob>.Instance);

        // Act
        await job.RunAnalysisAsync();

        // Assert — VendorPart's 1-day lead time means stock is sufficient.
        db.ReorderSuggestions.Should().NotContain(s => s.PartId == part.Id);
    }
}
