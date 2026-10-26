using System.Security.Claims;

using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Api.Features.Expenses;
using Forge.Api.Jobs;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums;
using Forge.Core.Enums.Accounting;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;
using Forge.Data.Repositories;
using Forge.Integrations;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// Expense approval journals on real Postgres: rejecting an approved expense reverses its approval entry in the same
/// transaction and a re-approval posts a fresh one; recurring expenses are filed as their owner through the same
/// approval path, self-approved only when <c>expense_self_approval</c> allows it, and post once.
/// </summary>
[Collection(PostgresCollection.Name)]
public sealed class ExpenseApprovalJournalPostgresTests(PostgresFixture fixture)
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int CashId = 100;
    private const int ExpenseAccountId = 107;
    private const int OwnerId = 42;
    private const int ApproverId = 7;

    private static readonly DateTimeOffset Now = new(2026, 1, 20, 15, 0, 0, TimeSpan.Zero);

    private sealed class FullGlOn : ICapabilitySnapshotProvider
    {
        public CapabilitySnapshot Current { get; } = new(
            new Dictionary<string, bool>(StringComparer.Ordinal) { ["CAP-ACCT-FULLGL"] = true },
            DateTimeOffset.UtcNow);
        public bool IsEnabled(string code) => Current.IsEnabled(code);
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => Now;
    }

    private static IHttpContextAccessor HttpContextFor(int userId)
        => new HttpContextAccessor
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())])),
            },
        };

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new AcctNumberSequenceAllocator(db), new SystemClock());

    private static UpdateExpenseStatusHandler StatusHandler(AppDbContext db, int actingUserId)
        => new(
            new ExpenseRepository(db),
            HttpContextFor(actingUserId),
            new Mock<ISyncQueueRepository>().Object,
            new Mock<IAccountingProviderFactory>().Object,
            NullLogger<UpdateExpenseStatusHandler>.Instance,
            new ExpenseApPostingService(db, Engine(db), new FullGlOn()),
            billPromotion: null,
            db: db);

    private static Task ResetAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT tablename FROM pg_tables
            WHERE schemaname = 'public' AND tablename <> '__EFMigrationsHistory') LOOP
    EXECUTE 'TRUNCATE TABLE ' || quote_ident(r.tablename) || ' RESTART IDENTITY CASCADE';
  END LOOP;
END $$;");

    private static Task SyncIdentitiesAsync(AppDbContext db)
        => db.Database.ExecuteSqlRawAsync(@"
DO $$
DECLARE r RECORD;
BEGIN
  FOR r IN (SELECT c.table_name, c.column_name FROM information_schema.columns c
            WHERE c.table_schema = 'public' AND c.is_identity = 'YES') LOOP
    EXECUTE format('SELECT setval(pg_get_serial_sequence(%L, %L), COALESCE((SELECT MAX(%I) FROM %I), 0) + 1, false)',
                   r.table_name, r.column_name, r.column_name, r.table_name);
  END LOOP;
END $$;");

    private static async Task SeedAsync(AppDbContext db, bool allowSelfApproval)
    {
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$", IsBaseCurrency = true });
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
            new GlAccount { Id = CashId, BookId = BookId, AccountNumber = "10100", Name = "Cash", AccountType = AccountType.Asset, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = ExpenseAccountId, BookId = BookId, AccountNumber = "60000", Name = "General & Administrative", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "CASH", GlAccountId = CashId },
            new AccountDeterminationRule { BookId = BookId, Key = "OPERATING_EXPENSE", GlAccountId = ExpenseAccountId });
        db.SystemSettings.Add(new SystemSetting { Key = "expense_self_approval", Value = allowSelfApproval ? "true" : "false" });
        await db.SaveChangesAsync();
        await SyncIdentitiesAsync(db);
    }

    private static async Task<decimal> ExpenseBalanceAsync(AppDbContext db)
        => await db.JournalLines.AsNoTracking()
            .Where(l => l.GlAccountId == ExpenseAccountId)
            .SumAsync(l => l.Debit - l.Credit);

    [Fact]
    public async Task Rejecting_an_approved_expense_reverses_its_journal_and_reapproval_posts_again()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db, allowSelfApproval: false);

        var expense = new Expense
        {
            UserId = OwnerId, Amount = 75m, Category = "Travel", Description = "Conference airfare",
            Status = ExpenseStatus.Pending, ExpenseDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero),
            SettlementTarget = ExpenseSettlementTarget.Cash,
        };
        db.Expenses.Add(expense);
        await db.SaveChangesAsync();

        await StatusHandler(db, ApproverId).Handle(new UpdateExpenseStatusCommand(expense.Id,
            new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, "looks good")), CancellationToken.None);
        (await ExpenseBalanceAsync(db)).Should().Be(75m);

        await StatusHandler(db, ApproverId).Handle(new UpdateExpenseStatusCommand(expense.Id,
            new UpdateExpenseStatusRequestModel(ExpenseStatus.Rejected, "Not a business trip after all")), CancellationToken.None);

        (await ExpenseBalanceAsync(db)).Should().Be(0m);
        var original = await db.JournalEntries.AsNoTracking()
            .SingleAsync(e => e.IdempotencyKey == $"AP:Expense:{expense.Id}:EXPENSE");
        original.Status.Should().Be(JournalEntryStatus.Reversed);

        await StatusHandler(db, ApproverId).Handle(new UpdateExpenseStatusCommand(expense.Id,
            new UpdateExpenseStatusRequestModel(ExpenseStatus.Approved, "resubmitted with the agenda")), CancellationToken.None);

        (await ExpenseBalanceAsync(db)).Should().Be(75m);
        (await db.JournalEntries.AsNoTracking()
            .SingleAsync(e => e.IdempotencyKey == $"AP:Expense:{expense.Id}:R1:EXPENSE")).Status
            .Should().Be(JournalEntryStatus.Posted);
    }

    private static RecurringExpenseJob Job(AppDbContext db)
    {
        var mediator = new Mock<IMediator>();
        mediator.Setup(m => m.Send(It.IsAny<GetExpenseSettingsQuery>(), It.IsAny<CancellationToken>()))
            .Returns((GetExpenseSettingsQuery q, CancellationToken ct) => new GetExpenseSettingsHandler(db).Handle(q, ct));
        mediator.Setup(m => m.Send(It.IsAny<UpdateExpenseStatusCommand>(), It.IsAny<CancellationToken>()))
            .Returns((UpdateExpenseStatusCommand c, CancellationToken ct) => StatusHandler(db, 0).Handle(c, ct));
        var approvals = new Mock<IApprovalService>();
        approvals.Setup(a => a.SubmitForApprovalAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<int>(),
                It.IsAny<decimal?>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalRequest?)null);
        return new RecurringExpenseJob(db, new FixedClock(), mediator.Object, approvals.Object, NullLogger<RecurringExpenseJob>.Instance);
    }

    private static async Task<int> AddTemplateAsync(AppDbContext db)
    {
        var template = new RecurringExpense
        {
            UserId = OwnerId, Amount = 49m, Category = "Software", Classification = "Subscription",
            Description = "CAD seat", Frequency = RecurrenceFrequency.Monthly,
            NextOccurrenceDate = new DateTimeOffset(2026, 1, 15, 0, 0, 0, TimeSpan.Zero), AutoApprove = true,
        };
        db.RecurringExpenses.Add(template);
        await db.SaveChangesAsync();
        return template.Id;
    }

    [Fact]
    public async Task Recurring_auto_approve_self_approves_and_posts_once_when_the_setting_allows_it()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db, allowSelfApproval: true);
        await AddTemplateAsync(db);

        await Job(db).GenerateDueExpensesAsync();
        await Job(db).GenerateDueExpensesAsync();

        var expense = await db.Expenses.AsNoTracking().SingleAsync();
        expense.Status.Should().Be(ExpenseStatus.SelfApproved);
        expense.ApprovedBy.Should().Be(OwnerId);
        (await db.JournalEntries.AsNoTracking().CountAsync(e => e.SourceType == "Expense" && e.SourceId == expense.Id)).Should().Be(1);
        (await ExpenseBalanceAsync(db)).Should().Be(49m);
    }

    [Fact]
    public async Task Recurring_auto_approve_stays_pending_without_self_approval_and_posts_nothing()
    {
        await using var db = fixture.CreateContext();
        await ResetAsync(db);
        await SeedAsync(db, allowSelfApproval: false);
        await AddTemplateAsync(db);

        await Job(db).GenerateDueExpensesAsync();

        var expense = await db.Expenses.AsNoTracking().SingleAsync();
        expense.Status.Should().Be(ExpenseStatus.Pending);
        (await db.JournalEntries.AsNoTracking().AnyAsync()).Should().BeFalse();
    }
}
