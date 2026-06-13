using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-2 STAGE E — material-issue WIP posting. Proves: DARK by default; an issue relieves the perpetual
/// valuation store at weighted-average and posts Dr INVENTORY_WIP / Cr INVENTORY_{class}; scrap debits
/// OPERATING_EXPENSE; a return reverses the move and re-credits the store; no store row falls back to the
/// issue's recorded unit cost; idempotent; non-stocked parts no-op.
/// </summary>
public class Phase2MaterialIssuePostingServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int OpenPeriodId = 1000;

    private const int InvRawId = 130;
    private const int InvWipId = 131;
    private const int InvFgId = 132;
    private const int OpExId = 600;

    private static readonly DateOnly EntryDate = new(2026, 1, 15);

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

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static MaterialIssuePostingService Service(AppDbContext db, bool fullGlOn)
        => new(db, Engine(db), new FakeCapabilities(fullGlOn), valuation: new InventoryValuationService(db));

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
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open,
        });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod
        {
            Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open,
        });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = InvRawId, BookId = BookId, AccountNumber = "13100", Name = "Inventory — Raw", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvWipId, BookId = BookId, AccountNumber = "13200", Name = "Inventory — WIP", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = InvFgId, BookId = BookId, AccountNumber = "13300", Name = "Inventory — FG", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory, IsPostable = true, IsActive = true },
            new GlAccount { Id = OpExId, BookId = BookId, AccountNumber = "60000", Name = "G&A", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = InvRawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = InvWipId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = InvFgId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = OpExId });

        await db.SaveChangesAsync();
        return db;
    }

    /// <summary>Adds a Part of the given class and a persisted MaterialIssue; returns the issue id.</summary>
    private static async Task<int> AddIssueAsync(
        AppDbContext db, InventoryClass cls, decimal qty, decimal unitCost,
        MaterialIssueType type = MaterialIssueType.Issue)
    {
        var part = new Part
        {
            PartNumber = $"P-{cls}-{Guid.NewGuid():N}", Description = "x",
            InventoryClass = cls, ProcurementSource = ProcurementSource.Buy,
        };
        db.Set<Part>().Add(part);
        await db.SaveChangesAsync();

        var issue = new MaterialIssue
        {
            JobId = 555, PartId = part.Id, Quantity = qty, UnitCost = unitCost,
            IssuedById = 7, IssuedAt = DateTimeOffset.UtcNow, IssueType = type,
        };
        db.Set<MaterialIssue>().Add(issue);
        await db.SaveChangesAsync();
        return issue.Id;
    }

    /// <summary>Seeds a perpetual valuation row for a part (BookId is the single test book).</summary>
    private static async Task SeedValuationAsync(AppDbContext db, int partId, decimal qty, decimal totalValue)
    {
        db.Set<InventoryValuation>().Add(new InventoryValuation
        {
            BookId = BookId, PartId = partId, OnHandQuantity = qty, TotalValue = totalValue,
            AverageUnitCost = qty != 0m ? Math.Round(totalValue / qty, 6) : 0m,
        });
        await db.SaveChangesAsync();
    }

    [Fact]
    public async Task Issue_WhenFullGlOff_IsNoOp()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Raw, 4m, 5m);

        await Service(db, fullGlOn: false).PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task Issue_WithStoreRow_PostsWipFromWeightedAverage_AndRelievesStore()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Raw, 4m, 99m); // unit cost ignored — store row wins
        var issue = await db.Set<MaterialIssue>().FindAsync(issueId);
        await SeedValuationAsync(db, issue!.PartId, qty: 10m, totalValue: 50m); // avg 5.00

        await Service(db, fullGlOn: true).PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Source.Should().Be(JournalSource.Inventory);
        entry.SourceType.Should().Be("MaterialIssue");
        entry.Lines.Single(l => l.GlAccountId == InvWipId).Debit.Should().Be(20m); // 4 × 5.00
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Credit.Should().Be(20m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));

        var store = await db.Set<InventoryValuation>().SingleAsync(v => v.PartId == issue.PartId);
        store.OnHandQuantity.Should().Be(6m);   // 10 − 4
        store.TotalValue.Should().Be(30m);       // 50 − 20
    }

    [Fact]
    public async Task Issue_NoStoreRow_FallsBackToUnitCost()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Raw, 3m, 7m); // no valuation row → 3 × 7 = 21

        await Service(db, fullGlOn: true).PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == InvWipId).Debit.Should().Be(21m);
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Credit.Should().Be(21m);
        (await db.Set<InventoryValuation>().AnyAsync()).Should().BeFalse("no store row existed, so none is created");
    }

    [Fact]
    public async Task Scrap_DebitsOperatingExpense()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Raw, 2m, 5m, MaterialIssueType.Scrap);
        var issue = await db.Set<MaterialIssue>().FindAsync(issueId);
        await SeedValuationAsync(db, issue!.PartId, qty: 10m, totalValue: 50m);

        await Service(db, fullGlOn: true).PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        entry.Lines.Single(l => l.GlAccountId == OpExId).Debit.Should().Be(10m); // 2 × 5
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Credit.Should().Be(10m);
    }

    [Fact]
    public async Task Return_ReversesIssue_AndCreditsStore()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Raw, 2m, 5m, MaterialIssueType.Return);
        var issue = await db.Set<MaterialIssue>().FindAsync(issueId);
        await SeedValuationAsync(db, issue!.PartId, qty: 6m, totalValue: 30m); // avg 5.00

        await Service(db, fullGlOn: true).PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines).SingleAsync();
        // Reverse of an issue: Dr INVENTORY_RAW / Cr INVENTORY_WIP at the issue's unit cost (2 × 5 = 10).
        entry.Lines.Single(l => l.GlAccountId == InvRawId).Debit.Should().Be(10m);
        entry.Lines.Single(l => l.GlAccountId == InvWipId).Credit.Should().Be(10m);

        var store = await db.Set<InventoryValuation>().SingleAsync(v => v.PartId == issue.PartId);
        store.OnHandQuantity.Should().Be(8m);   // 6 + 2
        store.TotalValue.Should().Be(40m);       // 30 + 10
    }

    [Fact]
    public async Task Issue_CalledTwice_IsIdempotent_StoreRelievedOnce()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Raw, 4m, 5m);
        var issue = await db.Set<MaterialIssue>().FindAsync(issueId);
        await SeedValuationAsync(db, issue!.PartId, qty: 10m, totalValue: 50m);
        var service = Service(db, fullGlOn: true);

        await service.PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);
        await service.PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().CountAsync()).Should().Be(1);
        var store = await db.Set<InventoryValuation>().SingleAsync(v => v.PartId == issue.PartId);
        store.OnHandQuantity.Should().Be(6m, "the store is relieved exactly once despite the re-post");
    }

    [Fact]
    public async Task Issue_NonStockedConsumable_IsNoOp()
    {
        using var db = await SeedAsync();
        var issueId = await AddIssueAsync(db, InventoryClass.Consumable, 5m, 2m);

        await Service(db, fullGlOn: true).PostMaterialIssueAsync(issueId, EntryDate, issuedByUserId: 7);

        (await db.JournalEntries.IgnoreQueryFilters().AnyAsync()).Should().BeFalse();
    }
}
