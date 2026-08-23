using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Verifies the Chart-of-Accounts management handlers against real Postgres: create validation
/// (normal-balance convention + unique number), safe-field edits, structural edits gated on
/// no-postings, the has-postings lock, and the control-account guard.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class GlAccountManagementPostgresTests(PostgresFixture fixture)
{
    private async Task<int> SeedBookAsync(int bookId)
    {
        await using var db = fixture.CreateContext();
        var cur = new Currency { Code = $"U{bookId}", Name = "US Dollar", Symbol = "$" };
        db.Set<Currency>().Add(cur);
        await db.SaveChangesAsync();
        var book = new Book { Code = $"BK{bookId}", Name = "Book", FunctionalCurrencyId = cur.Id, ReportingTimeZone = "UTC", RoundingTolerance = 0.01m, IsActive = true, RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime };
        db.Set<Book>().Add(book);
        await db.SaveChangesAsync();
        return book.Id;
    }

    [Fact]
    public async Task Create_enforces_normal_balance_and_uniqueness_then_persists()
    {
        var bookId = await SeedBookAsync(9101);

        await using (var db = fixture.CreateContext())
        {
            var handler = new CreateGlAccountHandler(db);

            // Wrong normal balance for an Expense (should be Debit) → rejected.
            var bad = new CreateGlAccountCommand(bookId, "60100", "Bad", AccountType.Expense, NormalBalance.Credit, null, false, false, null, null);
            await FluentActions.Awaiting(() => handler.Handle(bad, CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();

            // Valid create.
            var ok = new CreateGlAccountCommand(bookId, "60100", "Shop Supplies", AccountType.Expense, NormalBalance.Debit, null, false, false, CashFlowCategory.Operating, "Consumables");
            var created = await handler.Handle(ok, CancellationToken.None);
            created.AccountNumber.Should().Be("60100");
            created.IsControlAccount.Should().BeFalse();
            created.IsPostable.Should().BeTrue();
            created.HasPostings.Should().BeFalse();

            // Duplicate number in the same book → rejected.
            await FluentActions.Awaiting(() => handler.Handle(ok, CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }

    [Fact]
    public async Task Update_edits_safe_fields_and_structural_when_unposted_but_locks_after_postings()
    {
        var bookId = await SeedBookAsync(9102);
        int accountId;

        await using (var db = fixture.CreateContext())
        {
            var created = await new CreateGlAccountHandler(db).Handle(
                new CreateGlAccountCommand(bookId, "60200", "Freight", AccountType.Expense, NormalBalance.Debit, null, false, false, null, null),
                CancellationToken.None);
            accountId = created.Id;

            // Safe-field + structural edit while unposted — number change applies.
            var upd = new UpdateGlAccountCommand(accountId, "Freight In", null, true, false, CashFlowCategory.Operating, "Inbound freight", true, "60250", AccountType.Expense, NormalBalance.Debit);
            var updated = await new UpdateGlAccountHandler(db).Handle(upd, CancellationToken.None);
            updated.Name.Should().Be("Freight In");
            updated.AccountNumber.Should().Be("60250");
            updated.RequiresJob.Should().BeTrue();
        }

        // Post a journal line against the account.
        await using (var db = fixture.CreateContext())
        {
            var fy = new FiscalYear { BookId = bookId, Name = "FY", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open };
            db.Set<FiscalYear>().Add(fy); await db.SaveChangesAsync();
            var fp = new FiscalPeriod { FiscalYearId = fy.Id, PeriodNumber = 1, Name = "P1", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 1, 31), Status = FiscalPeriodStatus.Open };
            db.Set<FiscalPeriod>().Add(fp); await db.SaveChangesAsync();
            var cur = await db.Set<Book>().Where(b => b.Id == bookId).Select(b => b.FunctionalCurrencyId).FirstAsync();
            var je = new JournalEntry { BookId = bookId, EntryNumber = 1, EntryDate = new DateOnly(2026, 1, 15), FiscalPeriodId = fp.Id, FiscalYearId = fy.Id, Source = JournalSource.Manual, CurrencyId = cur, Status = JournalEntryStatus.Posted };
            db.Set<JournalEntry>().Add(je); await db.SaveChangesAsync();
            db.Set<JournalLine>().Add(new JournalLine { JournalEntryId = je.Id, BookId = bookId, LineNumber = 1, GlAccountId = accountId, Debit = 100m, Credit = 0m, CurrencyId = cur, TxnAmount = 100m, FunctionalAmount = 100m, FxRate = 1m });
            await db.SaveChangesAsync();
        }

        // Structural edit now locked; safe fields still editable.
        await using (var db = fixture.CreateContext())
        {
            var handler = new UpdateGlAccountHandler(db);
            var structural = new UpdateGlAccountCommand(accountId, "Freight In", null, true, false, CashFlowCategory.Operating, null, true, "60999", null, null);
            await FluentActions.Awaiting(() => handler.Handle(structural, CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();

            var safe = new UpdateGlAccountCommand(accountId, "Freight & Duty", null, false, false, CashFlowCategory.Operating, "renamed", true, null, null, null);
            var ok = await handler.Handle(safe, CancellationToken.None);
            ok.Name.Should().Be("Freight & Duty");
            ok.HasPostings.Should().BeTrue();
        }
    }

    [Fact]
    public async Task Update_rejects_editing_a_control_account()
    {
        var bookId = await SeedBookAsync(9103);
        int controlId;
        await using (var seed = fixture.CreateContext())
        {
            var ap = new GlAccount { BookId = bookId, AccountNumber = "20000", Name = "AP", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsControlAccount = true, ControlType = ControlAccountType.AP, IsPostable = true, IsActive = true };
            seed.GlAccounts.Add(ap); await seed.SaveChangesAsync(); controlId = ap.Id;
        }
        await using (var db = fixture.CreateContext())
        {
            var cmd = new UpdateGlAccountCommand(controlId, "AP renamed", null, false, false, null, null, true, null, null, null);
            await FluentActions.Awaiting(() => new UpdateGlAccountHandler(db).Handle(cmd, CancellationToken.None))
                .Should().ThrowAsync<InvalidOperationException>();
        }
    }
}
