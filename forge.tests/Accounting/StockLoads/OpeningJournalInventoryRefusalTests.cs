using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Services;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Integrations;

using static Forge.Tests.Helpers.InventoryLedgerTestBook;

namespace Forge.Tests.Accounting.StockLoads;

/// <summary>
/// Both opening-journal paths refuse an inventory control line with <c>OPENING_INVENTORY_USE_STOCK_LOAD</c>
/// (opening stock loads as a costed stock load instead), while a journal already posted is returned untouched
/// by an idempotent re-run.
/// </summary>
public sealed class OpeningJournalInventoryRefusalTests
{
    private static ImportOpeningJournalCommand Csv(params OpeningJournalLineModel[] lines)
        => new(BookId, StockLoadTestContext.AsOf, 1, lines);

    private static ConversionService Conversion(StockLoadTestContext ctx)
        => new(ctx.Db, Engine(ctx.Db), new BusinessIdentifierService(ctx.Db, new SystemClock()));

    [Fact]
    public async Task Csv_import_refuses_an_inventory_account_and_names_it()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var handler = new ImportOpeningJournalHandler(ctx.Db, Engine(ctx.Db), UserAccessor(7));

        var act = () => handler.Handle(Csv(
            new OpeningJournalLineModel("13100", 132_900m, 0m),
            new OpeningJournalLineModel("39000", 0m, 132_900m)), CancellationToken.None);

        var ex = (await act.Should().ThrowAsync<PostingException>()).Which;
        ex.Code.Should().Be("OPENING_INVENTORY_USE_STOCK_LOAD");
        ex.Message.Should().Contain("13100 Inventory — Raw").And.Contain("Opening stock");
        (await ctx.Db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Csv_import_rerun_returns_the_posted_journal_before_the_check()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var handler = new ImportOpeningJournalHandler(ctx.Db, Engine(ctx.Db), UserAccessor(7));
        var first = await handler.Handle(Csv(
            new OpeningJournalLineModel("52000", 10m, 0m),
            new OpeningJournalLineModel("39000", 0m, 10m)), CancellationToken.None);

        var again = await handler.Handle(Csv(
            new OpeningJournalLineModel("13100", 10m, 0m),
            new OpeningJournalLineModel("39000", 0m, 10m)), CancellationToken.None);

        again.EntryNumber.Should().Be(first.EntryNumber);
    }

    [Fact]
    public async Task Opening_balances_refuse_inventory_by_key_or_by_account()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();

        var byKey = () => Conversion(ctx).PostOpeningBalancesAsync(new PostOpeningBalancesModel(BookId, StockLoadTestContext.AsOf,
            [
                new OpeningBalanceLineModel("INVENTORY_FG", null, 50m, 0m, null, null, "Stock"),
                new OpeningBalanceLineModel("OPENING_BALANCE_EQUITY", null, 0m, 50m, null, null, "Equity"),
            ]), 7);
        (await byKey.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("OPENING_INVENTORY_USE_STOCK_LOAD");

        var byAccount = () => Conversion(ctx).PostOpeningBalancesAsync(new PostOpeningBalancesModel(BookId, StockLoadTestContext.AsOf,
            [
                new OpeningBalanceLineModel(null, RawId, 50m, 0m, null, null, "Stock"),
                new OpeningBalanceLineModel(null, StockLoadTestContext.ObeId, 0m, 50m, null, null, "Equity"),
            ]), 7);
        (await byAccount.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("OPENING_INVENTORY_USE_STOCK_LOAD");
    }

    [Fact]
    public async Task Opening_balances_rerun_returns_the_posted_journal_before_the_check()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();
        var first = await Conversion(ctx).PostOpeningBalancesAsync(new PostOpeningBalancesModel(BookId, StockLoadTestContext.AsOf,
            [
                new OpeningBalanceLineModel(null, WritedownId, 50m, 0m, null, null, "Expense"),
                new OpeningBalanceLineModel("OPENING_BALANCE_EQUITY", null, 0m, 50m, null, null, "Equity"),
            ]), 7);

        var again = await Conversion(ctx).PostOpeningBalancesAsync(new PostOpeningBalancesModel(BookId, StockLoadTestContext.AsOf,
            [
                new OpeningBalanceLineModel("INVENTORY_RAW", null, 50m, 0m, null, null, "Stock"),
                new OpeningBalanceLineModel("OPENING_BALANCE_EQUITY", null, 0m, 50m, null, null, "Equity"),
            ]), 7);

        again.JournalEntryId.Should().Be(first.JournalEntryId);
    }

    [Fact]
    public async Task Conversion_accounts_flag_inventory_control_rows()
    {
        using var ctx = await StockLoadTestContext.CreateAsync();

        var accounts = await new GetConversionAccountsHandler(ctx.Db)
            .Handle(new GetConversionAccountsQuery(BookId), CancellationToken.None);

        accounts.Single(a => a.AccountNumber == "13100").IsInventoryControl.Should().BeTrue();
        accounts.Single(a => a.AccountNumber == "39000").IsInventoryControl.Should().BeFalse();
    }
}
