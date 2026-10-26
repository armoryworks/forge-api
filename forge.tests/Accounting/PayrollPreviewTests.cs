using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities;
using Forge.Core.Entities.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Settings;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// PAY-001 previews: the register dry run (which headers fed which category, rows with net mismatches,
/// totals, blocking problems — and nothing saved) and the pay-run journal preview (the same lines the post
/// writes, each with its resolved account, unmapped keys left without one). The list carries the posted
/// entry number.
/// </summary>
public class PayrollPreviewTests
{
    private sealed class NoSettings : ISettingsService
    {
        public Task<string?> GetStringAsync(string key, CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<bool> GetBoolAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> GetIntAsync(string key, CancellationToken ct = default) => Task.FromResult(0);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(new Dictionary<string, string?>());
        public Task<string?> GetCompanyNameAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
    }

    private const string Register = """
        Employee Name,Gross Pay,Federal Tax,State Tax,Social Security,Medicare,Employer FICA,Net Pay
        "Hartman, Daniel",3000.00,300.00,90.00,186.00,43.50,229.50,2380.50
        "Wilson, Lena",2000.00,180.00,60.00,124.00,29.00,153.00,1500.00
        """;

    [Fact]
    public void DescribeColumns_NamesTheHeadersBehindEachCategory()
    {
        var columns = PayrollRegisterParser.DescribeColumns(Register);

        columns["employee"].Should().Equal("Employee Name");
        columns["gross"].Should().Equal("Gross Pay");
        columns["ficaEmployee"].Should().Equal("Social Security", "Medicare");
        columns["employerTax"].Should().Equal("Employer FICA");
        columns["otherDeductions"].Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_ReturnsRowsTotalsAndNetMismatches_AndSavesNothing()
    {
        using var db = TestDbContextFactory.Create();
        var service = new PayrollRegisterImportService(db, new NoSettings());

        var preview = await service.PreviewAsync(Register);

        preview.Errors.Should().BeEmpty();
        preview.Rows.Should().HaveCount(2);
        preview.Rows[0].NetMismatch.Should().BeFalse();
        preview.Rows[1].NetMismatch.Should().BeTrue("2000 - 393 = 1607, not the reported 1500");
        preview.Rows[1].ComputedNet.Should().Be(1607m);
        preview.GrossWages.Should().Be(5000m);
        preview.EmployeeTaxWithheld.Should().Be(1012.50m);
        preview.EmployerTax.Should().Be(382.50m);
        preview.NetPay.Should().Be(3987.50m);
        (await db.PayRuns.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Preview_WithoutEmployeeOrGrossColumns_ReportsTheProblem()
    {
        using var db = TestDbContextFactory.Create();
        var service = new PayrollRegisterImportService(db, new NoSettings());

        var preview = await service.PreviewAsync("Who,Amount\nA,10\n");

        preview.Errors.Should().Equal(PayrollRegisterImportService.MissingRequiredColumns);
        preview.Rows.Should().BeEmpty();
    }

    [Fact]
    public async Task Preview_HeaderOnly_ReportsNoEmployeeRows()
    {
        using var db = TestDbContextFactory.Create();
        var service = new PayrollRegisterImportService(db, new NoSettings());

        var preview = await service.PreviewAsync("Employee,Gross,Net\n");

        preview.Errors.Should().Equal(PayrollRegisterImportService.NoEmployeeRows);
    }

    [Fact]
    public async Task JournalPreview_ResolvesMappedKeys_AndLeavesUnmappedOnesBlank()
    {
        using var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book { Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1, ReportingTimeZone = "UTC", IsActive = true });
        db.GlAccounts.Add(new GlAccount { Id = 600, BookId = 1, AccountNumber = "60100", Name = "Wages", AccountType = AccountType.Expense, NormalBalance = NormalBalance.Debit, IsPostable = true, IsActive = true });
        db.Set<AccountDeterminationRule>().Add(new AccountDeterminationRule { BookId = 1, Key = "WAGE_EXPENSE", GlAccountId = 600 });
        db.PayRuns.Add(new PayRun
        {
            Id = 7, BookId = 1, PayDate = new DateOnly(2026, 6, 15), PeriodStart = new DateOnly(2026, 6, 1),
            PeriodEnd = new DateOnly(2026, 6, 14), GrossWages = 5000m, EmployeeTaxWithheld = 1000m, EmployerTax = 380m,
        });
        await db.SaveChangesAsync();

        var lines = await new GetPayRunJournalPreviewHandler(db)
            .Handle(new GetPayRunJournalPreviewQuery(7), CancellationToken.None);

        lines.Should().HaveCount(5);
        lines.Sum(l => l.Debit).Should().Be(lines.Sum(l => l.Credit));
        var wages = lines.Single(l => l.AccountKey == "WAGE_EXPENSE");
        wages.AccountNumber.Should().Be("60100");
        wages.Debit.Should().Be(5000m);
        lines.Single(l => l.AccountKey == "NET_PAY_PAYABLE").Should().Match<Forge.Core.Models.Accounting.PayRunJournalPreviewLineModel>(
            l => l.Credit == 4000m && l.GlAccountId == null);
    }

    [Fact]
    public async Task JournalPreview_UnknownRun_IsNotFound()
    {
        using var db = TestDbContextFactory.Create();

        var act = () => new GetPayRunJournalPreviewHandler(db).Handle(new GetPayRunJournalPreviewQuery(404), CancellationToken.None);

        await act.Should().ThrowAsync<KeyNotFoundException>();
    }

    [Fact]
    public async Task List_CarriesThePostedEntryNumber()
    {
        using var db = TestDbContextFactory.Create();
        db.Set<Currency>().Add(new Currency { Id = 1, Code = "USD", Name = "US Dollar", Symbol = "$" });
        db.Set<Book>().Add(new Book { Id = 1, Code = "MAIN", Name = "Main", FunctionalCurrencyId = 1, ReportingTimeZone = "UTC", IsActive = true });
        var entry = new JournalEntry { BookId = 1, EntryNumber = 42, EntryDate = new DateOnly(2026, 6, 15), Status = JournalEntryStatus.Posted, Source = JournalSource.Payroll, CurrencyId = 1 };
        db.JournalEntries.Add(entry);
        await db.SaveChangesAsync();
        db.PayRuns.AddRange(
            new PayRun { BookId = 1, PayDate = new DateOnly(2026, 6, 15), GrossWages = 100m, Status = PayRunStatus.Posted, JournalEntryId = entry.Id },
            new PayRun { BookId = 1, PayDate = new DateOnly(2026, 6, 1), GrossWages = 100m });
        await db.SaveChangesAsync();

        var runs = await new PayrollService(db, null!).ListAsync(1);

        runs[0].JournalEntryNumber.Should().Be(42);
        runs[1].JournalEntryNumber.Should().BeNull();
    }
}
