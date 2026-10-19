using System.Security.Claims;

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Moq;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Data.Context;
using Forge.Integrations;

namespace Forge.Tests.Helpers;

/// <summary>
/// A one-book chart with the stocked inventory accounts, WIP and the inventory write-down account, plus an open
/// 2026 period, for tests of the inventory posting paths.
/// </summary>
public static class InventoryLedgerTestBook
{
    public const int BookId = 1;
    public const int RawId = 130;
    public const int WipId = 131;
    public const int SubassemblyId = 132;
    public const int FgId = 133;
    public const int WritedownId = 150;
    public const string FullGl = "CAP-ACCT-FULLGL";

    private sealed class Allocator(AppDbContext db) : IAcctNumberSequenceAllocator
    {
        public async Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => (await db.JournalEntries.IgnoreQueryFilters()
                .Where(e => e.BookId == bookId && e.FiscalYearId == fiscalYearId)
                .MaxAsync(e => (long?)e.EntryNumber, ct) ?? 0) + 1;
    }

    public static async Task SeedAsync(AppDbContext db, bool withWritedownRule = true)
    {
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1,
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
            Inventory(RawId, "13100", "Inventory — Raw"),
            Inventory(WipId, "13200", "Inventory — WIP"),
            Inventory(SubassemblyId, "13250", "Inventory — Subassemblies"),
            Inventory(FgId, "13300", "Inventory — FG"),
            new GlAccount { Id = WritedownId, BookId = BookId, AccountNumber = "52000", Name = "Inventory Write-Down", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_RAW", GlAccountId = RawId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WIP", GlAccountId = WipId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_SUBASSEMBLY", GlAccountId = SubassemblyId },
            new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_FG", GlAccountId = FgId });
        if (withWritedownRule)
            db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = BookId, Key = "INVENTORY_WRITEDOWN", GlAccountId = WritedownId });
        await db.SaveChangesAsync();
    }

    public static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new Allocator(db), new SystemClock());

    public static InventoryAdjustmentPostingService AdjustmentPosting(AppDbContext db, bool fullGlOn = true)
        => new(db, Engine(db), fullGlOn ? new StubCapabilitySnapshotProvider(FullGl) : StubCapabilitySnapshotProvider.Off,
            new InventoryValuationService(db));

    public static IHttpContextAccessor UserAccessor(int userId)
    {
        var accessor = new Mock<IHttpContextAccessor>();
        accessor.Setup(a => a.HttpContext).Returns(new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test")),
        });
        return accessor.Object;
    }

    public static Task<List<JournalLine>> LinesAsync(AppDbContext db, string idempotencyKey)
        => db.JournalLines.IgnoreQueryFilters()
            .Where(l => db.JournalEntries.IgnoreQueryFilters().Any(e => e.Id == l.JournalEntryId && e.IdempotencyKey == idempotencyKey))
            .ToListAsync();

    private static GlAccount Inventory(int id, string number, string name) => new()
    {
        Id = id, BookId = BookId, AccountNumber = number, Name = name, AccountType = AccountType.Asset,
        NormalBalance = NormalBalance.Debit, IsControlAccount = true, ControlType = ControlAccountType.Inventory,
        IsPostable = true, IsActive = true,
    };
}
