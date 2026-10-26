using FluentAssertions;
using FluentValidation;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.StockLoads;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Tests.Helpers;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Accounting.StockLoads;

/// <summary>
/// §7A opening inventory as a costed stock load: the post writes bins, the valuation store and Dr inventory /
/// Cr Opening Balance Equity per part class; the dry run catches what an opening load must not do; valuation-only
/// loads are capped by the opening journal's inventory debit; a void needs the stock still where it was put.
/// </summary>
public sealed class StockLoadServiceTests
{
    private static StockLoadLineInput Line(string part, string? location, decimal qty, decimal cost, string? lot = null)
        => new(part, location, lot, null, qty, cost);

    [Fact]
    public async Task Post_offsets_equity_per_inventory_account_and_the_store_ties_to_the_ledger()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity,
            Line("FG-310", "A1", 200m, 471.00m),
            Line("RM-100", "BIN-B1", 1000m, 94.20m));
        load.LoadNumber.Should().Be("SL-000001");
        load.TotalValue.Should().Be(188_400m);

        var posted = await ctx.PostAsync(load.Id);

        posted.Status.Should().Be(StockLoadStatus.Posted);
        posted.JournalEntryId.Should().NotBeNull();
        var lines = await LinesAsync(ctx.Db, $"Conversion:StockLoad:{load.Id}");
        lines.Single(l => l.GlAccountId == FgId).Debit.Should().Be(94_200m);
        lines.Single(l => l.GlAccountId == RawId).Debit.Should().Be(94_200m);
        lines.Single(l => l.GlAccountId == StockLoadTestContext.ObeId).Credit.Should().Be(188_400m);
        var entry = await ctx.Db.JournalEntries.IgnoreQueryFilters().SingleAsync(e => e.Id == posted.JournalEntryId);
        entry.Source.Should().Be(JournalSource.Conversion);
        entry.SourceType.Should().Be("StockLoad");
        entry.EntryDate.Should().Be(StockLoadTestContext.AsOf);

        var bins = await ctx.Db.BinContents.Where(c => c.RemovedAt == null).ToListAsync();
        bins.Should().HaveCount(2);
        bins.Single(b => b.EntityId == ctx.Fg.Id).Quantity.Should().Be(200m);
        (await ctx.Db.BinMovements.CountAsync(m => m.Reason == BinMovementReason.Adjustment && m.Notes!.Contains("SL-000001")))
            .Should().Be(2);
        var store = await ctx.Db.InventoryValuations.ToListAsync();
        store.Single(s => s.PartId == ctx.Fg.Id).TotalValue.Should().Be(94_200m);
        store.Single(s => s.PartId == ctx.Raw.Id).AverageUnitCost.Should().Be(94.20m);

        var rec = await new InventoryValuationService(ctx.Db).ReconcileAsync(BookId);
        rec.Variance.Should().Be(0m);
        rec.IsReconciled.Should().BeTrue();
        rec.ReconcilingItems.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_refuses_unknown_parts_and_locations_naming_each_row()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();

        var act = () => ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity,
            Line("NOPE-1", "A1", 1m, 1m),
            Line("RM-100", "Z9", 1m, 1m));

        var errors = (await act.Should().ThrowAsync<ValidationException>()).Which.Errors.ToList();
        errors.Should().Contain(e => e.ErrorCode == "stock-load-unknown-part" && e.PropertyName == "lines[0].partNumber");
        errors.Should().Contain(e => e.ErrorCode == "stock-load-unknown-location" && e.PropertyName == "lines[1].location");
    }

    [Fact]
    public async Task Dry_run_reports_the_opening_load_rules_without_writing()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        ctx.Capabilities = new StubCapabilitySnapshotProvider("CAP-INV-LOTS");
        ctx.Db.Parts.AddRange(
            new Part { PartNumber = "CON-1", Name = "Gloves", InventoryClass = InventoryClass.Consumable },
            new Part { PartNumber = "SER-1", Name = "Controller", InventoryClass = InventoryClass.FinishedGood, TraceabilityType = TraceabilityType.Serial },
            new Part { PartNumber = "LOT-1", Name = "Adhesive", InventoryClass = InventoryClass.Raw, TraceabilityType = TraceabilityType.Lot },
            new Part { PartNumber = "PH-1", Name = "Kit", InventoryClass = InventoryClass.Subassembly, ProcurementSource = ProcurementSource.Phantom });
        ctx.Db.BinContents.Add(new BinContent { LocationId = ctx.BinB.Id, EntityType = "part", EntityId = ctx.Raw.Id, Quantity = 5m, PlacedAt = DateTimeOffset.UtcNow });
        await ctx.Db.SaveChangesAsync();

        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity,
            Line("CON-1", "A1", 1m, 1m),
            Line("SER-1", "A1", 1m, 1m),
            Line("LOT-1", "A1", 1m, 1m),
            Line("PH-1", "A1", 1m, 1m),
            Line("FG-310", "A1", 1m, 400m),
            Line("FG-310", "A1", 1m, 471m),
            Line("RM-100", "B1", 1m, 0m));

        var result = await ctx.ValidateAsync(load.Id);

        result.IsValid.Should().BeFalse();
        Issue(result, 1).Should().Contain("stock-load-part-not-stocked");
        Issue(result, 2).Should().Contain("stock-load-serial-unsupported");
        Issue(result, 3).Should().Contain("stock-load-lot-required");
        Issue(result, 4).Should().Contain("stock-load-part-not-stocked");
        Issue(result, 5).Should().Contain("stock-load-standard-cost-differs");
        Issue(result, 6).Should().Contain("stock-load-duplicate-line");
        Issue(result, 7).Should().Contain(["stock-load-location-not-empty", "stock-load-zero-cost"]);
        result.Issues.Single(i => i.Code == "stock-load-zero-cost").Severity.Should().Be(StockLoadIssueSeverity.Warning);
        (await ctx.Db.InventoryValuations.CountAsync()).Should().Be(0);

        var post = () => ctx.PostAsync(load.Id);
        (await post.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().Contain(e => e.ErrorCode == "stock-load-location-not-empty" && e.PropertyName == "lines[6]");
        (await ctx.Db.BinContents.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task Dry_run_refuses_connected_mode_and_an_unmapped_offset()
    {
        using var ctx = await StockLoadTestContext.CreateAsync(withOpeningEquity: false);
        ctx.Capabilities = new StubCapabilitySnapshotProvider("CAP-ACCT-EXTERNAL");
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity, Line("RM-100", "A1", 1m, 2m));

        var result = await ctx.ValidateAsync(load.Id);

        result.Issues.Select(i => i.Code).Should().Contain(["stock-load-connected-mode", "stock-load-unmapped-account"]);
        result.IsValid.Should().BeFalse();
    }

    [Fact]
    public async Task Valuation_only_is_capped_by_the_opening_journal_and_reconciles_with_no_journal()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        await Engine(ctx.Db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = StockLoadTestContext.AsOf, Source = JournalSource.Conversion,
            SourceType = "OpeningJournal", CurrencyId = 1, IdempotencyKey = $"Conversion:Book:{BookId}:OPENING",
            Lines =
            [
                new PostingLine { GlAccountId = RawId, Debit = 1000m },
                new PostingLine { GlAccountId = StockLoadTestContext.ObeId, Credit = 1000m },
            ],
        }, 7);

        var load = await ctx.CreateLoadAsync(StockLoadGlMode.ValuationOnly, Line("RM-100", "A1", 10m, 120m));
        (await ctx.ValidateAsync(load.Id)).Issues.Should().Contain(i => i.Code == "stock-load-exceeds-opening-journal");

        await new UpdateStockLoadHandler(ctx.Db, ctx.Service()).Handle(new UpdateStockLoadCommand(
            load.Id, StockLoadTestContext.AsOf, StockLoadGlMode.ValuationOnly, null, null, [Line("RM-100", "A1", 10m, 100m)]),
            CancellationToken.None);
        var posted = await ctx.PostAsync(load.Id);

        posted.JournalEntryId.Should().BeNull();
        (await ctx.Db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var rec = await new InventoryValuationService(ctx.Db).ReconcileAsync(BookId);
        rec.StoreValue.Should().Be(1000m);
        rec.Variance.Should().Be(0m);
        rec.ReconcilingItems.Should().BeEmpty();

        var second = await ctx.CreateLoadAsync(StockLoadGlMode.ValuationOnly, Line("RM-100", "B1", 1m, 1m));
        (await ctx.ValidateAsync(second.Id)).Issues.Should().Contain(i => i.Code == "stock-load-exceeds-opening-journal");
    }

    [Fact]
    public async Task Valuation_only_needs_an_opening_journal_with_inventory()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.ValuationOnly, Line("RM-100", "A1", 1m, 1m));

        (await ctx.ValidateAsync(load.Id)).Issues.Should().Contain(i => i.Code == "stock-load-no-opening-inventory");
    }

    [Fact]
    public async Task Void_needs_the_stock_still_on_hand_then_takes_everything_back_out()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity, Line("RM-100", "A1", 10m, 5m));
        await ctx.PostAsync(load.Id);
        var content = await ctx.Db.BinContents.SingleAsync();
        content.Quantity = 9m;
        await ctx.Db.SaveChangesAsync();

        var blocked = () => ctx.VoidAsync(load.Id);
        (await blocked.Should().ThrowAsync<ValidationException>())
            .Which.Errors.Should().ContainSingle(e => e.ErrorCode == "stock-load-void-stock-moved");

        content.Quantity = 10m;
        await ctx.Db.SaveChangesAsync();
        var voided = await ctx.VoidAsync(load.Id);

        voided.Status.Should().Be(StockLoadStatus.Voided);
        (await ctx.Db.BinContents.SingleAsync()).RemovedAt.Should().NotBeNull();
        (await ctx.Db.InventoryValuations.SingleAsync()).OnHandQuantity.Should().Be(0m);
        var counter = await LinesAsync(ctx.Db, $"Conversion:StockLoad:{load.Id}:VOID");
        counter.Single(l => l.GlAccountId == RawId).Credit.Should().Be(50m);
        counter.Single(l => l.GlAccountId == StockLoadTestContext.ObeId).Debit.Should().Be(50m);
        var rec = await new InventoryValuationService(ctx.Db).ReconcileAsync(BookId);
        rec.Variance.Should().Be(0m);
        rec.ReconcilingItems.Should().BeEmpty();

        var again = () => ctx.VoidAsync(load.Id);
        await again.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Void_after_the_journal_was_reversed_by_hand_relieves_the_ledger_only_once()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity, Line("RM-100", "A1", 10m, 5m));
        var posted = await ctx.PostAsync(load.Id);
        await Engine(ctx.Db).ReverseAsync(posted.JournalEntryId!.Value, StockLoadTestContext.AsOf, "Posted in error", 7);

        await ctx.VoidAsync(load.Id);

        (await ctx.Db.JournalEntries.IgnoreQueryFilters()
            .AnyAsync(e => e.IdempotencyKey == $"Conversion:StockLoad:{load.Id}:VOID")).Should().BeFalse();
        var rec = await new InventoryValuationService(ctx.Db).ReconcileAsync(BookId);
        rec.StoreValue.Should().Be(0m);
        rec.GlInventoryBalance.Should().Be(0m);
    }

    [Fact]
    public async Task Offset_equity_warns_when_the_opening_journal_already_debited_the_account()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        await Engine(ctx.Db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = StockLoadTestContext.AsOf, Source = JournalSource.Conversion,
            SourceType = "OpeningJournal", CurrencyId = 1, IdempotencyKey = $"Conversion:Book:{BookId}:OPENING",
            Lines =
            [
                new PostingLine { GlAccountId = RawId, Debit = 1000m },
                new PostingLine { GlAccountId = StockLoadTestContext.ObeId, Credit = 1000m },
            ],
        }, 7);

        var raw = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity, Line("RM-100", "A1", 10m, 100m));
        var fg = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity, Line("FG-310", "A1", 1m, 471m));

        var result = await ctx.ValidateAsync(raw.Id);
        result.IsValid.Should().BeTrue();
        result.Issues.Should().ContainSingle(i => i.Code == "stock-load-opening-journal-has-inventory"
            && i.Severity == StockLoadIssueSeverity.Warning);
        (await ctx.ValidateAsync(fg.Id)).Issues.Should().NotContain(i => i.Code == "stock-load-opening-journal-has-inventory");
    }

    [Fact]
    public async Task Only_a_draft_can_be_changed_posted_or_deleted()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity, Line("RM-100", null, 1m, 1m));
        load.Lines.Single().StorageLocationId.Should().BeGreaterThan(0);
        await ctx.PostAsync(load.Id);

        var post = () => ctx.PostAsync(load.Id);
        await post.Should().ThrowAsync<InvalidOperationException>();
        var delete = () => new DeleteStockLoadHandler(ctx.Db, new Forge.Integrations.SystemClock())
            .Handle(new DeleteStockLoadCommand(load.Id), CancellationToken.None);
        await delete.Should().ThrowAsync<InvalidOperationException>();
        var update = () => new UpdateStockLoadHandler(ctx.Db, ctx.Service()).Handle(new UpdateStockLoadCommand(
            load.Id, StockLoadTestContext.AsOf, StockLoadGlMode.OffsetEquity, null, null, [Line("RM-100", "A1", 1m, 1m)]),
            CancellationToken.None);
        await update.Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task Lot_lines_create_the_lot_record_and_the_as_of_date_is_checked_against_the_opening_journal()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        ctx.Capabilities = new StubCapabilitySnapshotProvider("CAP-INV-LOTS");
        var lotted = new Part { PartNumber = "LOT-2", Name = "Resin lot", InventoryClass = InventoryClass.Raw, TraceabilityType = TraceabilityType.Lot };
        ctx.Db.Parts.Add(lotted);
        await ctx.Db.SaveChangesAsync();
        await Engine(ctx.Db).PostAsync(new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 2, 1), Source = JournalSource.Conversion,
            SourceType = "OpeningJournal", CurrencyId = 1, IdempotencyKey = $"Conversion:Book:{BookId}:OPENING",
            Lines =
            [
                new PostingLine { GlAccountId = WritedownId, Debit = 10m },
                new PostingLine { GlAccountId = StockLoadTestContext.ObeId, Credit = 10m },
            ],
        }, 7);
        var load = await ctx.CreateLoadAsync(StockLoadGlMode.OffsetEquity,
            new StockLoadLineInput("LOT-2", "A1", "L-77", new DateOnly(2027, 6, 30), 4m, 2.5m));

        var result = await ctx.ValidateAsync(load.Id);
        result.IsValid.Should().BeTrue();
        result.Issues.Should().ContainSingle(i => i.Code == "stock-load-date-differs");
        await ctx.PostAsync(load.Id);

        var lot = await ctx.Db.LotRecords.SingleAsync();
        lot.LotNumber.Should().Be("L-77");
        lot.Quantity.Should().Be(4m);
        lot.ExpirationDate.Should().Be(new DateTimeOffset(2027, 6, 30, 0, 0, 0, TimeSpan.Zero));
        (await ctx.Db.BinContents.SingleAsync()).LotNumber.Should().Be("L-77");
    }

    [Fact]
    public async Task Csv_template_text_creates_the_same_lines_and_bad_cells_name_their_row()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        const string csv = "partNumber,location,lot,expirationDate,quantity,unitCost\nFG-310,A1,,,200,471.00\nRM-100,BIN-B1,,,1000,94.20\n";

        var load = await new CreateStockLoadHandler(ctx.Db, ctx.Service(), UserAccessor(7)).Handle(
            new CreateStockLoadCommand(BookId, StockLoadTestContext.AsOf, StockLoadGlMode.OffsetEquity, null, "opening.csv", null, csv),
            CancellationToken.None);

        load.Lines.Should().HaveCount(2);
        load.TotalValue.Should().Be(188_400m);
        load.SourceFileName.Should().Be("opening.csv");

        var (_, errors) = StockLoadCsvParser.Parse("partNumber,quantity,unitCost,expirationDate\nRM-100,ten,1,13/45/2026\n");
        errors.Select(e => e.PropertyName).Should().BeEquivalentTo(["lines[0].quantity", "lines[0].expirationDate"]);
    }

    private static List<string> Issue(StockLoadValidationResult result, int line)
        => result.Issues.Where(i => i.LineNumber == line).Select(i => i.Code).ToList();
}
