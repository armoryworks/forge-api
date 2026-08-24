using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Verifies the <c>acct_budgets</c> filtered unique index
/// (<c>ux_acct_budgets_book_account_year_period WHERE deleted_at IS NULL</c>)
/// against real Postgres — the InMemory provider models no such constraint. Uses a
/// non-null <c>PeriodMonth</c> because Postgres treats NULLs as distinct, so the
/// index only guards monthly slots at the DB level (the full-year, month-null slot
/// is guarded app-side by the upsert). Proves: a duplicate live slot is rejected,
/// and soft-deleting frees the slot for re-creation.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class BudgetPostgresTests(PostgresFixture fixture)
{
    private async Task<(int bookId, int accountId)> SeedBookAndAccountAsync(int seed)
    {
        await using var db = fixture.CreateContext();
        var cur = new Currency { Code = $"C{seed}", Name = "US Dollar", Symbol = "$" };
        db.Set<Currency>().Add(cur);
        await db.SaveChangesAsync();

        var book = new Book
        {
            Code = $"BK{seed}", Name = "Book", FunctionalCurrencyId = cur.Id,
            ReportingTimeZone = "UTC", RoundingTolerance = 0.01m, IsActive = true,
            RevenueRecognitionMethod = RevenueRecognitionMethod.PointInTime,
        };
        db.Set<Book>().Add(book);
        await db.SaveChangesAsync();

        var account = new GlAccount
        {
            BookId = book.Id, AccountNumber = $"6{seed}", Name = "Rent Expense",
            AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit,
            IsPostable = true, IsActive = true,
        };
        db.Set<GlAccount>().Add(account);
        await db.SaveChangesAsync();

        return (book.Id, account.Id);
    }

    [Fact]
    public async Task Duplicate_live_monthly_slot_is_rejected_by_unique_index()
    {
        var (bookId, accountId) = await SeedBookAndAccountAsync(8801);

        await using var db = fixture.CreateContext();
        db.AcctBudgets.Add(new AcctBudget { BookId = bookId, GlAccountId = accountId, FiscalYear = 2026, PeriodMonth = 1, Amount = 100m });
        await db.SaveChangesAsync();

        db.AcctBudgets.Add(new AcctBudget { BookId = bookId, GlAccountId = accountId, FiscalYear = 2026, PeriodMonth = 1, Amount = 200m });
        await FluentActions.Awaiting(() => db.SaveChangesAsync())
            .Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Soft_deleting_frees_the_slot_for_recreation()
    {
        var (bookId, accountId) = await SeedBookAndAccountAsync(8802);

        int firstId;
        await using (var db = fixture.CreateContext())
        {
            var budget = new AcctBudget { BookId = bookId, GlAccountId = accountId, FiscalYear = 2026, PeriodMonth = 3, Amount = 100m };
            db.AcctBudgets.Add(budget);
            await db.SaveChangesAsync();
            firstId = budget.Id;

            budget.DeletedAt = DateTimeOffset.UtcNow;
            await db.SaveChangesAsync();
        }

        await using (var db = fixture.CreateContext())
        {
            db.AcctBudgets.Add(new AcctBudget { BookId = bookId, GlAccountId = accountId, FiscalYear = 2026, PeriodMonth = 3, Amount = 250m });
            await FluentActions.Awaiting(() => db.SaveChangesAsync()).Should().NotThrowAsync();

            // The soft-deleted original is filtered out; the live row is the new one.
            var live = await db.AcctBudgets.Where(b => b.BookId == bookId && b.GlAccountId == accountId && b.PeriodMonth == 3).ToListAsync();
            live.Should().ContainSingle();
            live[0].Id.Should().NotBe(firstId);
            live[0].Amount.Should().Be(250m);
        }
    }
}
