using System.Security.Claims;

using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Quality;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// NCR scrap posting. Proves: dark while CAP-ACCT-FULLGL is off; a Scrap disposition draws the held lot down and
/// writes the scrapped value off as Dr INVENTORY_WRITEDOWN / Cr INVENTORY_{class} at the store's weighted
/// average, relieving the store; no store row falls back to the latest purchase price; idempotent per NCR;
/// non-stocked parts post nothing.
/// </summary>
public class NcrScrapPostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int UserId = 7;
    private const string Lot = "LOT-S";

    private const int InvRawId = 130;
    private const int InvFgId = 132;
    private const int WritedownId = 520;

    private static readonly DateTimeOffset Now = new(2026, 3, 10, 15, 0, 0, TimeSpan.Zero);

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private sealed class FakeCapabilities(bool fullGlOn) : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = fullGlOn },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private static NcrScrapPostingService Service(AppDbContext db, bool fullGlOn)
        => new(db,
            new ForgeGlPostingEngine(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock()),
            new FakeCapabilities(fullGlOn),
            new InventoryValuationService(db));

    private static IHttpContextAccessor Accessor()
    {
        var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, UserId.ToString())], "Test");
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext { User = new ClaimsPrincipal(identity) });
        return accessor.Object;
    }

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear
        {
            Id = 10, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = 1000, FiscalYearId = 10, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvFgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — FG", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = WritedownId, BookId = BookId, AccountNumber = "52000", Name = "Inventory Write-Down", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = InvRawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InvFgId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WRITEDOWN", GlAccountId = WritedownId });

        await db.SaveChangesAsync();
        return db;
    }

    private static async Task<NonConformance> NcrAsync(AppDbContext db, InventoryClass cls, decimal heldQuantity)
    {
        var part = new Part { PartNumber = $"P-{cls}", Name = "Bracket", InventoryClass = cls, ProcurementSource = ProcurementSource.Buy };
        var bin = new StorageLocation { Name = "A1", LocationType = LocationType.Bin, IsActive = true };
        db.Parts.Add(part);
        db.StorageLocations.Add(bin);
        await db.SaveChangesAsync();

        db.BinContents.Add(new BinContent
        {
            LocationId = bin.Id, EntityType = "part", EntityId = part.Id, Quantity = heldQuantity,
            LotNumber = Lot, Status = BinContentStatus.QcHold, PlacedAt = Now.AddDays(-1),
        });
        var ncr = new NonConformance
        {
            NcrNumber = "NCR-20260310-001", Type = NcrType.Internal, PartId = part.Id, LotNumber = Lot,
            DetectedById = UserId, DetectedAt = Now, Description = "Cracked", AffectedQuantity = 4,
            Status = NcrStatus.Contained,
        };
        db.NonConformances.Add(ncr);
        await db.SaveChangesAsync();
        return ncr;
    }

    private static async Task SeedValuationAsync(AppDbContext db, int partId, decimal qty, decimal totalValue)
    {
        db.Set<InventoryValuation>().Add(new InventoryValuation
        {
            BookId = BookId, PartId = partId, OnHandQuantity = qty, TotalValue = totalValue,
            AverageUnitCost = Math.Round(totalValue / qty, 6),
        });
        await db.SaveChangesAsync();
    }

    private static Task DispositionScrapAsync(AppDbContext db, int ncrId, bool fullGlOn)
        => new DispositionNcrHandler(db, Mock.Of<IClock>(c => c.UtcNow == Now), Accessor(), Service(db, fullGlOn))
            .Handle(new DispositionNcrCommand(ncrId, new DispositionNcrRequestModel { Code = NcrDispositionCode.Scrap }),
                CancellationToken.None);

    [Fact]
    public async Task ScrapDisposition_WhenFullGlOff_DrawsStockDown_AndPostsNothing()
    {
        using var db = await SeedAsync();
        var ncr = await NcrAsync(db, InventoryClass.Raw, 10);

        await DispositionScrapAsync(db, ncr.Id, fullGlOn: false);

        (await db.BinContents.SingleAsync()).Quantity.Should().Be(6);
        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task ScrapDisposition_WritesTheScrappedValueOff_AtWeightedAverage_AndRelievesTheStore()
    {
        using var db = await SeedAsync();
        var ncr = await NcrAsync(db, InventoryClass.FinishedGood, 10);
        await SeedValuationAsync(db, ncr.PartId, qty: 10m, totalValue: 25m);

        await DispositionScrapAsync(db, ncr.Id, fullGlOn: true);

        (await db.BinContents.SingleAsync()).Quantity.Should().Be(6);
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.Inventory);
        entry.SourceType.Should().Be("NonConformance");
        entry.SourceId.Should().Be(ncr.Id);
        entry.EntryDate.Should().Be(new DateOnly(2026, 3, 10));
        entry.Lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(10m);
        entry.Lines.Single(l => l.GlAccountId == InvFgId).Credit.Should().Be(10m);

        var store = await db.Set<InventoryValuation>().SingleAsync();
        store.OnHandQuantity.Should().Be(6m);
        store.TotalValue.Should().Be(15m);
    }

    [Fact]
    public async Task NoStoreRow_FallsBackToTheLatestPurchasePrice()
    {
        using var db = await SeedAsync();
        var ncr = await NcrAsync(db, InventoryClass.Raw, 10);
        db.PurchaseOrderLines.Add(new PurchaseOrderLine { PurchaseOrderId = 1, PartId = ncr.PartId, Description = "x", OrderedQuantity = 10, UnitPrice = 3m });
        await db.SaveChangesAsync();

        await Service(db, fullGlOn: true).PostScrapAsync(ncr.Id, 4m, new DateOnly(2026, 3, 10), UserId);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == WritedownId).Debit.Should().Be(12m);
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Credit.Should().Be(12m);
        (await db.Set<InventoryValuation>().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Repost_IsANoOp_AndDoesNotRelieveTheStoreTwice()
    {
        using var db = await SeedAsync();
        var ncr = await NcrAsync(db, InventoryClass.Raw, 10);
        await SeedValuationAsync(db, ncr.PartId, qty: 10m, totalValue: 50m);
        var service = Service(db, fullGlOn: true);

        await service.PostScrapAsync(ncr.Id, 4m, new DateOnly(2026, 3, 10), UserId);
        await service.PostScrapAsync(ncr.Id, 4m, new DateOnly(2026, 3, 10), UserId);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        (await db.Set<InventoryValuation>().SingleAsync()).OnHandQuantity.Should().Be(6m);
    }

    [Fact]
    public async Task NonStockedPart_PostsNothing()
    {
        using var db = await SeedAsync();
        var ncr = await NcrAsync(db, InventoryClass.Consumable, 10);

        await Service(db, fullGlOn: true).PostScrapAsync(ncr.Id, 4m, new DateOnly(2026, 3, 10), UserId);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }
}
