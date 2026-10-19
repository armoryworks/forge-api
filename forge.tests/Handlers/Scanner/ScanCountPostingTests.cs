using FluentAssertions;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Auth;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Handlers.Scanner;

public class ScanCountPostingTests
{
    private const int UserId = 7;
    private const string Pin = "2468";

    private static async Task<(AppDbContext Db, int PartId, int BinId)> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        await InventoryLedgerTestBook.SeedAsync(db);
        var part = new Part { PartNumber = "ORING-018", Description = "O-ring", InventoryClass = InventoryClass.Raw, ManualCostOverride = 0.10m };
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        db.Parts.Add(part);
        db.StorageLocations.Add(bin);
        db.Users.Add(new ApplicationUser
        {
            Id = UserId, UserName = "scanner", Email = "scanner@forge.local", FirstName = "Scan", LastName = "Ner",
            PinHash = SetPinHandler.HashPin(Pin),
        });
        await db.SaveChangesAsync();
        db.BinContents.Add(new BinContent { LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = 100m, PlacedAt = DateTimeOffset.UtcNow });
        db.Set<InventoryValuation>().Add(new InventoryValuation { BookId = BookId, PartId = part.Id, OnHandQuantity = 100m, AverageUnitCost = 0.10m, TotalValue = 10m });
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();
        return (db, part.Id, bin.Id);
    }

    private static Task<int> CountAsync(AppDbContext db, int partId, int binId, decimal actual)
        => new ExecuteScanCountHandler(db, new SystemClock(), UserAccessor(UserId), AdjustmentPosting(db)).Handle(
            new ExecuteScanCountCommand(new ScanCountRequestModel(partId, binId, actual)), CancellationToken.None);

    private static Task ReverseAsync(AppDbContext db, int scanLogId)
        => new ReverseScanActionHandler(db, new SystemClock(), UserAccessor(UserId), MaterialIssueMediator.For(db), AdjustmentPosting(db)).Handle(
            new ReverseScanActionCommand(new ScanReversalRequestModel(scanLogId, Pin)), CancellationToken.None);

    private static async Task<decimal> RawBalanceAsync(AppDbContext db)
        => await db.JournalLines.IgnoreQueryFilters().Where(l => l.GlAccountId == RawId).SumAsync(l => l.Debit - l.Credit);

    [Fact]
    public async Task Count_Short_PostsTheShortage()
    {
        var (db, partId, binId) = await SeedAsync();
        using var _ = db;

        var logId = await CountAsync(db, partId, binId, 90m);

        var lines = await LinesAsync(db, $"Inventory:ScanCount:{logId}:ADJUST");
        lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(1.00m);
        lines.Single(l => l.GlAccountId == RawId).Credit.Should().Be(1.00m);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(90m);
    }

    [Fact]
    public async Task Count_WithALargeVariance_NotifiesWithoutPaddedDecimals()
    {
        var (db, partId, binId) = await SeedAsync();
        using var _ = db;
        db.Roles.Add(new IdentityRole<int> { Id = 9, Name = "Manager", NormalizedName = "MANAGER" });
        db.UserRoles.Add(new IdentityUserRole<int> { UserId = UserId, RoleId = 9 });
        await db.SaveChangesAsync();

        await CountAsync(db, partId, binId, 50.5000m);

        (await db.Notifications.SingleAsync()).Message.Should()
            .StartWith("Part ORING-018: expected 100, counted 50.5 (variance");
    }

    [Fact]
    public async Task ReverseCount_PostsTheOppositeAdjustment_SoTheLedgerFollowsTheBin()
    {
        var (db, partId, binId) = await SeedAsync();
        using var _ = db;
        var logId = await CountAsync(db, partId, binId, 90m);
        db.ChangeTracker.Clear();

        await ReverseAsync(db, logId);

        db.ChangeTracker.Clear();
        (await db.BinContents.Where(b => b.EntityId == partId && b.RemovedAt == null).SumAsync(b => b.Quantity)).Should().Be(100m);
        var lines = await LinesAsync(db, $"Inventory:ScanCountReversal:{logId}:ADJUST");
        lines.Single(l => l.GlAccountId == RawId).Debit.Should().Be(1.00m);
        lines.Single(l => l.GlAccountId == WritedownId).Credit.Should().Be(1.00m);
        (await RawBalanceAsync(db)).Should().Be(0m);
        var store = await db.InventoryValuations.SingleAsync();
        store.OnHandQuantity.Should().Be(100m);
        store.TotalValue.Should().Be(10m);
    }

    [Fact]
    public async Task ReverseCount_OfAnOverage_PostsTheShortageBack()
    {
        var (db, partId, binId) = await SeedAsync();
        using var _ = db;
        var logId = await CountAsync(db, partId, binId, 105m);
        db.ChangeTracker.Clear();

        await ReverseAsync(db, logId);

        db.ChangeTracker.Clear();
        (await RawBalanceAsync(db)).Should().Be(0m);
        (await db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(100m);
    }
}
