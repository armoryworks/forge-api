using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models.Accounting;
using Forge.Data.Context;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Phase-0 posting-engine coverage (§5.2, §5.9): balanced/unbalanced,
/// period-lock (HardClosed reject, SoftClosed block + override), idempotency
/// (duplicate-key returns existing), reversal + double-reverse, control-line
/// party, book-consistency, single-currency invariant, ledger-always-balances,
/// and the immutability interceptor carve-out.
/// </summary>
public class PostingEngineTests
{
    private const int BookId = 1;
    private const int OtherBookId = 2;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;

    // Account ids seeded by Seed().
    private const int CashId = 100;       // Asset, postable, debit-normal
    private const int RevenueId = 101;    // Income, postable, credit-normal
    private const int ArControlId = 102;  // Asset, control (AR)
    private const int SummaryId = 103;    // non-postable header
    private const int InactiveId = 104;   // inactive
    private const int OtherBookAcctId = 200; // belongs to OtherBook

    private const int OpenPeriodId = 1000;
    private const int SoftClosedPeriodId = 1001;
    private const int HardClosedPeriodId = 1002;

    /// <summary>In-process allocator (the InMemory provider can't run the row-lock SQL).</summary>
    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine CreateEngine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();

        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });

        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<Book>().Add(new Book
        {
            Id = OtherBookId, Code = "OTHER", Name = "Other", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });

        var fy = new FiscalYear
        {
            Id = FiscalYearId, BookId = BookId, Name = "FY2026",
            StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31),
            Status = FiscalYearStatus.Open,
        };
        db.Set<FiscalYear>().Add(fy);

        db.Set<FiscalPeriod>().AddRange(
            new FiscalPeriod
            {
                Id = OpenPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "Jan 2026",
                StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31),
                Status = FiscalPeriodStatus.Open,
            },
            new FiscalPeriod
            {
                Id = SoftClosedPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 2, Name = "Feb 2026",
                StartDate = new DateOnly(2026, 2, 1), EndDate = new DateOnly(2026, 2, 28),
                Status = FiscalPeriodStatus.SoftClosed,
            },
            new FiscalPeriod
            {
                Id = HardClosedPeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 3, Name = "Mar 2026",
                StartDate = new DateOnly(2026, 3, 1), EndDate = new DateOnly(2026, 3, 31),
                Status = FiscalPeriodStatus.HardClosed,
            });

        db.Set<GlAccount>().AddRange(
            Account(CashId, "1000", "Cash", AccountType.Asset, NormalBalance.Debit),
            Account(RevenueId, "4000", "Revenue", AccountType.Income, NormalBalance.Credit),
            new GlAccount
            {
                Id = ArControlId, BookId = BookId, AccountNumber = "1200", Name = "Accounts Receivable",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsControlAccount = true, ControlType = ControlAccountType.AR, IsPostable = true, IsActive = true,
            },
            new GlAccount
            {
                Id = SummaryId, BookId = BookId, AccountNumber = "1", Name = "Assets (summary)",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsPostable = false, IsActive = true,
            },
            new GlAccount
            {
                Id = InactiveId, BookId = BookId, AccountNumber = "9999", Name = "Old Account",
                AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
                IsPostable = true, IsActive = false,
            },
            new GlAccount
            {
                Id = OtherBookAcctId, BookId = OtherBookId, AccountNumber = "1000", Name = "Other Cash",
                AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit,
                IsPostable = true, IsActive = true,
            });

        // Determination rules (global scope).
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "SALES_REVENUE", GlAccountId = RevenueId },
            new AccountDeterminationRule { BookId = BookId, Key = "AR_CONTROL", GlAccountId = ArControlId });

        await db.SaveChangesAsync();
        return db;
    }

    private static GlAccount Account(int id, string num, string name, AccountType type, NormalBalance nb)
        => new()
        {
            Id = id, BookId = BookId, AccountNumber = num, Name = name,
            AccountType = type, NormalBalance = nb, IsPostable = true, IsActive = true,
        };

    private static PostingRequest BalancedManual(DateOnly? date = null, decimal amount = 100m) => new()
    {
        BookId = BookId,
        EntryDate = date ?? new DateOnly(2026, 1, 15),
        Source = JournalSource.Manual,
        CurrencyId = UsdId,
        Memo = "Test JE",
        Lines =
        [
            new PostingLine { GlAccountId = CashId, Debit = amount },
            new PostingLine { GlAccountId = RevenueId, Credit = amount },
        ],
    };

    [Fact]
    public async Task PostAsync_BalancedManual_PostsAndMaintainsLedgerBalance()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var entry = await engine.PostAsync(BalancedManual(), postedByUserId: 7);

        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.EntryNumber.Should().Be(1);
        entry.FiscalPeriodId.Should().Be(OpenPeriodId);
        entry.FiscalYearId.Should().Be(FiscalYearId);
        entry.PostedBy.Should().Be(7);
        entry.PostedAt.Should().NotBeNull();
        entry.Lines.Should().HaveCount(2);
        entry.Lines.Should().OnlyContain(l => l.FxRate == 1m && l.FunctionalAmount == l.TxnAmount);

        var balances = await db.LedgerBalances.ToListAsync();
        balances.Should().HaveCount(2);
        balances.Single(b => b.GlAccountId == CashId).DebitTotal.Should().Be(100m);
        balances.Single(b => b.GlAccountId == RevenueId).CreditTotal.Should().Be(100m);
    }

    [Fact]
    public async Task PostAsync_Unbalanced_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = CashId, Debit = 100m },
                new PostingLine { GlAccountId = RevenueId, Credit = 90m },
            ],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("UNBALANCED");
    }

    [Fact]
    public async Task PostAsync_BothSidesNonZeroOnALine_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines = [new PostingLine { GlAccountId = CashId, Debit = 100m, Credit = 100m }],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DEBIT_CREDIT_XOR");
    }

    [Fact]
    public async Task PostAsync_IntoHardClosedPeriod_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var act = async () => await engine.PostAsync(BalancedManual(new DateOnly(2026, 3, 15)), 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_HARD_CLOSED");
    }

    [Fact]
    public async Task PostAsync_IntoSoftClosedPeriod_BlockedWithoutOverride()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var act = async () => await engine.PostAsync(BalancedManual(new DateOnly(2026, 2, 15)), 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_SOFT_CLOSED");
    }

    [Fact]
    public async Task PostAsync_IntoSoftClosedPeriod_AllowedWithOverride()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 2, 15), Source = JournalSource.Manual,
            CurrencyId = UsdId, AllowSoftClosedOverride = true,
            Lines =
            [
                new PostingLine { GlAccountId = CashId, Debit = 50m },
                new PostingLine { GlAccountId = RevenueId, Credit = 50m },
            ],
        };

        var entry = await engine.PostAsync(req, 1);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.FiscalPeriodId.Should().Be(SoftClosedPeriodId);
    }

    [Fact]
    public async Task PostAsync_NoPeriodCoversDate_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var act = async () => await engine.PostAsync(BalancedManual(new DateOnly(2025, 6, 1)), 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_NOT_FOUND");
    }

    [Fact]
    public async Task PostAsync_ControlLineWithoutParty_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = ArControlId, Debit = 100m }, // control, no party
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("CONTROL_LINE_PARTY_REQUIRED");
    }

    [Fact]
    public async Task PostAsync_ControlLineWithParty_Posts()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = ArControlId, Debit = 100m, PartyType = SubledgerPartyType.Customer, PartyId = 55 },
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var entry = await engine.PostAsync(req, 1);
        entry.Status.Should().Be(JournalEntryStatus.Posted);
        entry.Lines.Single(l => l.GlAccountId == ArControlId).SubledgerPartyId.Should().Be(55);
    }

    [Fact]
    public async Task PostAsync_NonPostableAccount_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = SummaryId, Debit = 100m },
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("ACCOUNT_NOT_POSTABLE");
    }

    [Fact]
    public async Task PostAsync_CrossBookAccount_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = OtherBookAcctId, Debit = 100m }, // belongs to OtherBook
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("BOOK_MISMATCH_ACCOUNT");
    }

    [Fact]
    public async Task PostAsync_ResolvesByDeterminationKey()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { AccountKey = "CASH", Debit = 100m },
                new PostingLine { AccountKey = "SALES_REVENUE", Credit = 100m },
            ],
        };

        var entry = await engine.PostAsync(req, 1);
        entry.Lines.Single(l => l.Debit > 0).GlAccountId.Should().Be(CashId);
        entry.Lines.Single(l => l.Credit > 0).GlAccountId.Should().Be(RevenueId);
    }

    [Fact]
    public async Task PostAsync_UnmappedKey_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.Manual, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { AccountKey = "DOES_NOT_EXIST", Debit = 100m },
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("DETERMINATION_UNMAPPED");
    }

    [Fact]
    public async Task PostAsync_NonManualSourceWithoutIdempotencyKey_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.AR, CurrencyId = UsdId,
            Lines =
            [
                new PostingLine { GlAccountId = CashId, Debit = 100m },
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var act = async () => await engine.PostAsync(req, 1);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("IDEMPOTENCY_KEY_REQUIRED");
    }

    [Fact]
    public async Task PostAsync_DuplicateIdempotencyKey_ReturnsExistingNoThrow()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var req = new PostingRequest
        {
            BookId = BookId, EntryDate = new DateOnly(2026, 1, 15), Source = JournalSource.AR, CurrencyId = UsdId,
            IdempotencyKey = "AR:Invoice:42:REVENUE",
            Lines =
            [
                new PostingLine { GlAccountId = CashId, Debit = 100m },
                new PostingLine { GlAccountId = RevenueId, Credit = 100m },
            ],
        };

        var first = await engine.PostAsync(req, 1);
        var second = await engine.PostAsync(req, 1);

        second.Id.Should().Be(first.Id);
        (await db.JournalEntries.CountAsync()).Should().Be(1);
    }

    [Fact]
    public async Task ReverseAsync_PostsOppositeAndFlipsOriginal()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var original = await engine.PostAsync(BalancedManual(), 1);
        var reversal = await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "correction", 9);

        var reloaded = await db.JournalEntries.IgnoreQueryFilters()
            .Include(e => e.Lines).FirstAsync(e => e.Id == original.Id);
        reloaded.Status.Should().Be(JournalEntryStatus.Reversed);
        reloaded.ReversedByEntryId.Should().Be(reversal.Id);

        reversal.ReversalOfEntryId.Should().Be(original.Id);
        // Dr/Cr swapped.
        reversal.Lines.Single(l => l.GlAccountId == CashId).Credit.Should().Be(100m);
        reversal.Lines.Single(l => l.GlAccountId == RevenueId).Debit.Should().Be(100m);

        // Ledger balances net to zero after a reversal (no special case).
        var cash = await db.LedgerBalances.SingleAsync(b => b.GlAccountId == CashId);
        (cash.DebitTotal - cash.CreditTotal).Should().Be(0m);
    }

    [Fact]
    public async Task ReverseAsync_DoubleReverse_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var original = await engine.PostAsync(BalancedManual(), 1);
        await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 20), "first", 9);

        var act = async () => await engine.ReverseAsync(original.Id, new DateOnly(2026, 1, 21), "second", 9);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("ALREADY_REVERSED");
    }

    [Fact]
    public async Task ReverseAsync_IntoHardClosedPeriod_Throws()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);

        var original = await engine.PostAsync(BalancedManual(), 1);
        var act = async () => await engine.ReverseAsync(original.Id, new DateOnly(2026, 3, 10), "x", 9);
        (await act.Should().ThrowAsync<PostingException>()).Which.Code.Should().Be("PERIOD_HARD_CLOSED");
    }

    [Fact]
    public async Task TrialBalance_Balances_AndNetsReversal()
    {
        using var db = await SeedAsync();
        var engine = CreateEngine(db);
        var tb = new TrialBalanceService(db);

        await engine.PostAsync(BalancedManual(amount: 250m), 1);

        var before = await tb.GetTrialBalanceAsync(BookId);
        before.IsBalanced.Should().BeTrue();
        before.TotalDebit.Should().Be(250m);
        before.TotalCredit.Should().Be(250m);

        // Reverse → the original (now Reversed) + the reversal (Posted) net out.
        var entry = await db.JournalEntries.IgnoreQueryFilters().FirstAsync();
        await engine.ReverseAsync(entry.Id, new DateOnly(2026, 1, 20), "x", 1);

        var after = await tb.GetTrialBalanceAsync(BookId);
        after.IsBalanced.Should().BeTrue();
        after.TotalDebit.Should().Be(after.TotalCredit);
        after.Rows.Sum(r => r.NetBalance).Should().Be(0m);
    }
}
