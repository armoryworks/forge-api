using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Handlers.Inventory;

public class ManualStockPostingTests : IDisposable
{
    private readonly AppDbContext _db = TestDbContextFactory.Create();
    private Part _part = null!;
    private StorageLocation _bin = null!;

    public void Dispose() => _db.Dispose();

    private async Task<BinContent> SeedAsync(decimal onHand = 100m, bool fullGl = true)
    {
        await InventoryLedgerTestBook.SeedAsync(_db);
        _part = new Part { PartNumber = "BOLT-10", Description = "Bolt", InventoryClass = InventoryClass.Raw, ManualCostOverride = 0.50m };
        _bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        _db.Parts.Add(_part);
        _db.StorageLocations.Add(_bin);
        await _db.SaveChangesAsync();
        var content = new BinContent { LocationId = _bin.Id, EntityType = "part", EntityId = _part.Id, Quantity = onHand, PlacedAt = DateTimeOffset.UtcNow };
        _db.BinContents.Add(content);
        _db.Set<InventoryValuation>().Add(new InventoryValuation { BookId = BookId, PartId = _part.Id, OnHandQuantity = onHand, AverageUnitCost = 0.50m, TotalValue = onHand * 0.50m });
        await _db.SaveChangesAsync();
        return content;
    }

    private InventoryRepository Repo => new(_db);

    private async Task<List<JournalLine>> PostedLinesAsync()
        => await _db.JournalLines.IgnoreQueryFilters()
            .Where(l => _db.JournalEntries.IgnoreQueryFilters()
                .Any(e => e.Id == l.JournalEntryId && e.SourceType == "BinMovement"))
            .ToListAsync();

    private async Task<decimal> StoreQuantityAsync()
        => (await _db.InventoryValuations.AsNoTracking().SingleAsync()).OnHandQuantity;

    [Fact]
    public async Task SetOnHand_Rise_PostsAnOverageAtTheStoreAverage()
    {
        await SeedAsync();
        var handler = new SetOnHandQuantityHandler(Repo, UserAccessor(7), new SystemClock(), _db, AdjustmentPosting(_db));

        await handler.Handle(new SetOnHandQuantityCommand(new SetOnHandQuantityRequestModel(
            _part.Id, _bin.Id, 110m, "Found stock", null, null, null)), CancellationToken.None);

        var lines = await PostedLinesAsync();
        lines.Single(l => l.GlAccountId == RawId).Debit.Should().Be(5.00m);
        lines.Single(l => l.GlAccountId == WritedownId).Credit.Should().Be(5.00m);
        (await StoreQuantityAsync()).Should().Be(110m);
    }

    [Fact]
    public async Task UseStock_PostsAShortageAndRelievesTheStore()
    {
        await SeedAsync();
        var handler = new UseStockHandler(Repo, UserAccessor(7), new SystemClock(), _db, AdjustmentPosting(_db));

        await handler.Handle(new UseStockCommand(new UseStockRequestModel(_part.Id, _bin.Id, 30m, null, null)), CancellationToken.None);

        var lines = await PostedLinesAsync();
        lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(15.00m);
        lines.Single(l => l.GlAccountId == RawId).Credit.Should().Be(15.00m);
        (await StoreQuantityAsync()).Should().Be(70m);
    }

    [Fact]
    public async Task ReceiveStock_PostsAnOverage()
    {
        await SeedAsync();
        var handler = new ReceiveStockHandler(Repo, UserAccessor(7), _db, AdjustmentPosting(_db));

        await handler.Handle(new ReceiveStockCommand(new ReceiveStockRequestModel(_part.Id, _bin.Id, 20m, null, null, null)), CancellationToken.None);

        (await PostedLinesAsync()).Single(l => l.GlAccountId == RawId).Debit.Should().Be(10.00m);
        (await StoreQuantityAsync()).Should().Be(120m);
    }

    [Fact]
    public async Task AdjustStock_Drop_PostsTheFractionalShortage()
    {
        var content = await SeedAsync(onHand: 10.5m);
        var handler = new AdjustStockHandler(Repo, UserAccessor(7), _db, AdjustmentPosting(_db));

        await handler.Handle(new AdjustStockCommand(new AdjustStockRequestModel(content.Id, 8, "Damaged", null)), CancellationToken.None);

        (await _db.BinMovements.SingleAsync()).Quantity.Should().Be(2.5m);
        (await PostedLinesAsync()).Single(l => l.GlAccountId == RawId).Credit.Should().Be(1.25m);
        (await StoreQuantityAsync()).Should().Be(8m);
    }

    [Fact]
    public async Task RemoveBinContent_ZeroesTheRowPostsTheShortage_AndRefusesASecondRemoval()
    {
        var content = await SeedAsync(onHand: 40m);
        var handler = new RemoveBinContentHandler(Repo, UserAccessor(7), _db, AdjustmentPosting(_db));

        await handler.Handle(new RemoveBinContentCommand(content.Id), CancellationToken.None);
        var again = () => handler.Handle(new RemoveBinContentCommand(content.Id), CancellationToken.None);

        await again.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already been removed*");
        var row = await _db.BinContents.AsNoTracking().SingleAsync(b => b.Id == content.Id);
        row.Quantity.Should().Be(0m);
        row.RemovedAt.Should().NotBeNull();
        (await _db.BinMovements.SingleAsync()).Quantity.Should().Be(40m);
        (await PostedLinesAsync()).Single(l => l.GlAccountId == RawId).Credit.Should().Be(20.00m);
        (await StoreQuantityAsync()).Should().Be(0m);
    }

    [Fact]
    public async Task PlaceBinContent_PostsALoosePart_ButNotJobOutputOrAnotherEntity()
    {
        await SeedAsync();
        var handler = new PlaceBinContentHandler(Repo, UserAccessor(7), _db, AdjustmentPosting(_db));
        PlaceBinContentCommand Place(string entityType, int? jobId) => new(new PlaceBinContentRequestModel(
            _bin.Id, entityType, _part.Id, 4m, null, jobId, BinContentStatus.Stored, null));

        await handler.Handle(Place("part", jobId: 9), CancellationToken.None);
        await handler.Handle(Place("tool", jobId: null), CancellationToken.None);
        (await PostedLinesAsync()).Should().BeEmpty();

        await handler.Handle(Place("part", jobId: null), CancellationToken.None);
        (await PostedLinesAsync()).Single(l => l.GlAccountId == RawId).Debit.Should().Be(2.00m);
        (await StoreQuantityAsync()).Should().Be(104m);
    }

    [Fact]
    public async Task ManualChanges_WithFullGlOff_MoveTheBinsOnly()
    {
        await SeedAsync();
        var handler = new UseStockHandler(Repo, UserAccessor(7), new SystemClock(), _db, AdjustmentPosting(_db, fullGlOn: false));

        await handler.Handle(new UseStockCommand(new UseStockRequestModel(_part.Id, _bin.Id, 30m, null, null)), CancellationToken.None);

        (await _db.BinContents.AsNoTracking().SumAsync(b => b.Quantity)).Should().Be(70m);
        _db.JournalEntries.Should().BeEmpty();
        (await StoreQuantityAsync()).Should().Be(100m);
    }
}
