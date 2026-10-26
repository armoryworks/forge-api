using Forge.Api.Features.Accounting;
using Forge.Api.Features.Accounting.StockLoads;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting.StockLoads;

/// <summary>
/// The inventory test book plus Opening Balance Equity, a finished good, a raw material and two bins, with
/// factories for the stock-load service and handlers.
/// </summary>
internal sealed class StockLoadTestContext : IDisposable
{
    public const int ObeId = 390;
    public static readonly DateOnly AsOf = new(2026, 1, 1);

    public AppDbContext Db { get; } = TestDbContextFactory.Create();
    public Part Fg { get; private set; } = null!;
    public Part Raw { get; private set; } = null!;
    public StorageLocation BinA { get; private set; } = null!;
    public StorageLocation BinB { get; private set; } = null!;
    public StubCapabilitySnapshotProvider Capabilities { get; set; } = new();

    public static async Task<StockLoadTestContext> CreateAsync(bool withOpeningEquity = true)
    {
        var ctx = new StockLoadTestContext();
        await InventoryLedgerTestBook.SeedAsync(ctx.Db);
        if (withOpeningEquity)
        {
            ctx.Db.Set<GlAccount>().Add(new GlAccount
            {
                Id = ObeId, BookId = InventoryLedgerTestBook.BookId, AccountNumber = "39000", Name = "Opening Balance Equity",
                AccountType = AccountType.Equity, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true,
            });
            ctx.Db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule
            {
                BookId = InventoryLedgerTestBook.BookId, Key = "OPENING_BALANCE_EQUITY", GlAccountId = ObeId,
            });
        }
        ctx.Fg = new Part { PartNumber = "FG-310", Name = "Housing", InventoryClass = InventoryClass.FinishedGood, ManualCostOverride = 471m };
        ctx.Raw = new Part { PartNumber = "RM-100", Name = "Resin", InventoryClass = InventoryClass.Raw };
        ctx.BinA = new StorageLocation { Name = "A1", Barcode = "BIN-A1", LocationType = LocationType.Bin, IsActive = true };
        ctx.BinB = new StorageLocation { Name = "B1", Barcode = "BIN-B1", LocationType = LocationType.Bin, IsActive = true };
        ctx.Db.AddRange(ctx.Fg, ctx.Raw, ctx.BinA, ctx.BinB);
        await ctx.Db.SaveChangesAsync();
        return ctx;
    }

    public StockLoadService Service() => new(
        Db, InventoryLedgerTestBook.Engine(Db), new InventoryValuationService(Db), new InventoryRepository(Db),
        Capabilities, new SystemClock());

    public async Task<StockLoadModel> CreateLoadAsync(
        StockLoadGlMode mode, params StockLoadLineInput[] lines)
        => await new CreateStockLoadHandler(Db, Service(), InventoryLedgerTestBook.UserAccessor(7))
            .Handle(new CreateStockLoadCommand(InventoryLedgerTestBook.BookId, AsOf, mode, null, null, lines), CancellationToken.None);

    public Task<StockLoadModel> PostAsync(int id)
        => new PostStockLoadHandler(Service(), InventoryLedgerTestBook.UserAccessor(7))
            .Handle(new PostStockLoadCommand(id), CancellationToken.None);

    public Task<StockLoadModel> VoidAsync(int id)
        => new VoidStockLoadHandler(Service(), InventoryLedgerTestBook.UserAccessor(7))
            .Handle(new VoidStockLoadCommand(id), CancellationToken.None);

    public Task<StockLoadValidationResult> ValidateAsync(int id)
        => new ValidateStockLoadHandler(Db, Service()).Handle(new ValidateStockLoadQuery(id), CancellationToken.None);

    public void Dispose() => Db.Dispose();
}
