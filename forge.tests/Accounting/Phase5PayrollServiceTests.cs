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
/// Phase-5 — payroll GL foundation. Proves net-pay derivation, the balanced payroll journal (Dr
/// wage/employer-tax expense; Cr employee-tax / employer-tax / net-pay payable), and the post lifecycle.
/// </summary>
public class Phase5PayrollServiceTests
{
    private const int BookId = 1;
    private const int UsdId = 1;
    private const int FiscalYearId = 10;
    private const int PeriodId = 1000;
    private const int WageId = 700;
    private const int EmployerTaxExpId = 701;
    private const int EmpTaxPayId = 702;
    private const int EmployerTaxPayId = 703;
    private const int NetPayId = 704;

    private sealed class FakeAllocator : IAcctNumberSequenceAllocator
    {
        private long _next = 1;
        public Task<long> AllocateNextAsync(int bookId, int fiscalYearId, CancellationToken ct = default)
            => Task.FromResult(_next++);
    }

    private static ForgeGlPostingEngine Engine(AppDbContext db)
        => new(db, new AccountDeterminationResolver(db), new FakeAllocator(), new SystemClock());

    private static PayrollService Service(AppDbContext db) => new(db, Engine(db));

    private static async Task<AppDbContext> SeedAsync()
    {
        var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = UsdId, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book
        {
            Id = BookId, Code = "MAIN", Name = "Main", FunctionalCurrencyId = UsdId,
            ReportingTimeZone = "America/New_York", RoundingTolerance = 0.01m, IsActive = true,
        });
        db.Set<FiscalYear>().Add(new FiscalYear { Id = FiscalYearId, BookId = BookId, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalYearStatus.Open });
        db.Set<FiscalPeriod>().Add(new FiscalPeriod { Id = PeriodId, FiscalYearId = FiscalYearId, PeriodNumber = 1, Name = "FY2026", StartDate = new DateOnly(2026, 1, 1), EndDate = new DateOnly(2026, 12, 31), Status = FiscalPeriodStatus.Open });
        db.Set<GlAccount>().AddRange(
            new GlAccount { Id = WageId, BookId = BookId, AccountNumber = "63000", Name = "Wage Expense", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = EmployerTaxExpId, BookId = BookId, AccountNumber = "63100", Name = "Employer Payroll Tax", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true },
            new GlAccount { Id = EmpTaxPayId, BookId = BookId, AccountNumber = "24000", Name = "Employee Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = EmployerTaxPayId, BookId = BookId, AccountNumber = "24100", Name = "Employer Tax Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true },
            new GlAccount { Id = NetPayId, BookId = BookId, AccountNumber = "24500", Name = "Net Pay Payable", AccountType = AccountType.Liability, NormalBalance = NormalBalance.Credit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().AddRange(
            new AccountDeterminationRule { BookId = BookId, Key = "WAGE_EXPENSE", GlAccountId = WageId },
            new AccountDeterminationRule { BookId = BookId, Key = "EMPLOYER_PAYROLL_TAX_EXPENSE", GlAccountId = EmployerTaxExpId },
            new AccountDeterminationRule { BookId = BookId, Key = "EMPLOYEE_TAX_PAYABLE", GlAccountId = EmpTaxPayId },
            new AccountDeterminationRule { BookId = BookId, Key = "EMPLOYER_TAX_PAYABLE", GlAccountId = EmployerTaxPayId },
            new AccountDeterminationRule { BookId = BookId, Key = "NET_PAY_PAYABLE", GlAccountId = NetPayId });
        await db.SaveChangesAsync();
        return db;
    }

    private static CreatePayRunModel RunModel(decimal gross = 10000m, decimal empTax = 2000m, decimal erTax = 800m) => new(
        BookId, new DateOnly(2026, 3, 15), new DateOnly(2026, 3, 1), new DateOnly(2026, 3, 15), gross, empTax, erTax);

    [Fact]
    public async Task CreatePayRun_DerivesNetPay()
    {
        using var db = await SeedAsync();
        var run = await Service(db).CreatePayRunAsync(RunModel());
        run.NetPay.Should().Be(8000m); // 10000 − 2000
        run.Status.Should().Be(PayRunStatus.Draft);
    }

    [Fact]
    public async Task PostPayRun_PostsBalancedPayrollJournal()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var run = await svc.CreatePayRunAsync(RunModel());

        var posted = await svc.PostPayRunAsync(run.Id, postedByUserId: 7);

        posted.Status.Should().Be(PayRunStatus.Posted);
        posted.JournalEntryId.Should().NotBeNull();
        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Id == posted.JournalEntryId);
        entry.Source.Should().Be(JournalSource.Payroll);
        entry.Lines.Single(l => l.GlAccountId == WageId).Debit.Should().Be(10000m);
        entry.Lines.Single(l => l.GlAccountId == EmployerTaxExpId).Debit.Should().Be(800m);
        entry.Lines.Single(l => l.GlAccountId == EmpTaxPayId).Credit.Should().Be(2000m);
        entry.Lines.Single(l => l.GlAccountId == EmployerTaxPayId).Credit.Should().Be(800m);
        entry.Lines.Single(l => l.GlAccountId == NetPayId).Credit.Should().Be(8000m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }

    [Fact]
    public async Task PostPayRun_AlreadyPosted_Throws()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var run = await svc.CreatePayRunAsync(RunModel());
        await svc.PostPayRunAsync(run.Id, 7);

        var act = async () => await svc.PostPayRunAsync(run.Id, 7);
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*already posted*");
    }

    [Fact]
    public async Task CreatePayRun_WithholdingExceedsGross_Throws()
    {
        using var db = await SeedAsync();
        var act = async () => await Service(db).CreatePayRunAsync(RunModel(gross: 1000m, empTax: 2000m, erTax: 0m));
        await act.Should().ThrowAsync<InvalidOperationException>().WithMessage("*cannot exceed gross*");
    }

    [Fact]
    public async Task PostPayRun_NoEmployerTax_OmitsEmployerLines()
    {
        using var db = await SeedAsync();
        var svc = Service(db);
        var run = await svc.CreatePayRunAsync(RunModel(gross: 5000m, empTax: 1000m, erTax: 0m));

        var posted = await svc.PostPayRunAsync(run.Id, 7);

        var entry = await db.JournalEntries.IgnoreQueryFilters().Include(e => e.Lines)
            .SingleAsync(e => e.Id == posted.JournalEntryId);
        entry.Lines.Should().NotContain(l => l.GlAccountId == EmployerTaxExpId);
        entry.Lines.Should().NotContain(l => l.GlAccountId == EmployerTaxPayId);
        entry.Lines.Single(l => l.GlAccountId == NetPayId).Credit.Should().Be(4000m);
        entry.Lines.Sum(l => l.Debit).Should().Be(entry.Lines.Sum(l => l.Credit));
    }
}
