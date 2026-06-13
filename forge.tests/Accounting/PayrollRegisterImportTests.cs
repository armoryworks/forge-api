using FluentAssertions;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Core.Entities.Accounting;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Tests.Helpers;

namespace Forge.Tests.Accounting;

/// <summary>
/// PAY-001 — provider-agnostic payroll register import (owner-ratified per-employee granularity).
/// Proves: synonym auto-detection incl. SUMMING split columns (Social Security + Medicare → FICA)
/// and the employer-vs-employee FICA header collision; settings overrides pin exact headers;
/// $/parens amounts; import creates a Draft pay run whose totals are Σ lines with the net
/// identity warning on mismatches. Also the aging-bucket ladder parser (accounting.aging.bucket-days).
/// </summary>
public class PayrollRegisterImportTests
{
    private sealed class FakeSettings(Dictionary<string, string?>? values = null) : ISettingsService
    {
        private readonly Dictionary<string, string?> _values = values ?? [];
        public Task<string?> GetStringAsync(string key, CancellationToken ct = default)
            => Task.FromResult(_values.TryGetValue(key, out var v) ? v : null);
        public Task<bool> GetBoolAsync(string key, CancellationToken ct = default) => Task.FromResult(false);
        public Task<int> GetIntAsync(string key, CancellationToken ct = default) => Task.FromResult(0);
        public Task SetAsync(string key, string? value, CancellationToken ct = default) => Task.CompletedTask;
        public Task<IReadOnlyDictionary<string, string?>> GetGroupAsync(string group, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyDictionary<string, string?>>(_values);
    }

    private const string AdpStyleRegister = """
        Employee Name,Gross Pay,Federal Tax,State Tax,Social Security,Medicare,401(k),Employer FICA,Net Pay
        "Hartman, Daniel",3000.00,300.00,90.00,186.00,43.50,150.00,229.50,"2,230.50"
        "Wilson, Lena",2000.00,180.00,60.00,124.00,29.00,0.00,153.00,"1,607.00"
        Totals,5000.00,480.00,150.00,310.00,72.50,150.00,382.50,
        """;

    [Fact]
    public void Parser_SynonymDetection_SumsSplitFica_SeparatesEmployerSide()
    {
        var rows = PayrollRegisterParser.Parse(AdpStyleRegister);

        rows.Should().HaveCount(2); // the trailing "Totals" summary row is excluded
        var dan = rows.Single(r => r.EmployeeName.StartsWith("Hartman"));
        dan.Gross.Should().Be(3000m);
        dan.Federal.Should().Be(300m);
        dan.State.Should().Be(90m);
        dan.FicaEmployee.Should().Be(229.50m);          // SS 186 + Medicare 43.50 summed
        dan.OtherDeductions.Should().Be(150m);          // 401(k)
        dan.EmployerTax.Should().Be(229.50m);           // "Employer FICA" NOT mixed into employee FICA
        dan.Net.Should().Be(2230.50m);
    }

    [Fact]
    public void Parser_Overrides_PinExactHeaders()
    {
        const string weird = """
            Person,Base Comp,W/H A,Take Home
            Smith J,1000.00,(100.00),1100.00
            """;
        var rows = PayrollRegisterParser.Parse(weird, new PayrollRegisterParser.ColumnOverrides(
            Employee: "Person", Gross: "Base Comp", Federal: "W/H A", Net: "Take Home"));

        rows.Should().ContainSingle();
        rows[0].Gross.Should().Be(1000m);
        rows[0].Federal.Should().Be(-100m);              // parenthesized negative honored
        rows[0].Net.Should().Be(1100m);
    }

    [Fact]
    public async Task Import_CreatesDraftRun_TotalsAreLineSums_WarnsOnNetMismatch()
    {
        var db = TestDbContextFactory.Create();
        var service = new PayrollRegisterImportService(db, new FakeSettings());

        // Lena's register net (1607) == computed; Dan's reported net is off by $10 → one warning.
        var register = AdpStyleRegister.Replace("\"2,230.50\"", "\"2,240.50\"");
        var result = await service.ImportAsync(
            1, new DateOnly(2026, 6, 19), new DateOnly(2026, 6, 1), new DateOnly(2026, 6, 15),
            register, userId: 1);

        result.GrossWages.Should().Be(5000m);            // 3000 + 2000; the Totals row excluded
        result.EmployeeCount.Should().Be(2);
        var run = await db.Set<PayRun>().Include(r => r.Lines).SingleAsync();
        run.Status.Should().Be(PayRunStatus.Draft);
        run.GrossWages.Should().Be(run.Lines.Sum(l => l.GrossPay));
        run.EmployeeTaxWithheld.Should().Be(run.Lines.Sum(l => l.TotalWithholdings));
        run.EmployerTax.Should().Be(run.Lines.Sum(l => l.EmployerTax));
        result.Warnings.Should().ContainSingle(w => w.Contains("Hartman"));
    }

    [Fact]
    public void AgingBuckets_DefaultAndCustomAndMalformed()
    {
        AgingBuckets.Parse("30,60,90").Select(b => b.Label)
            .Should().Equal("0-30", "31-60", "61-90", "91+");
        AgingBuckets.Parse("45,90,135").Select(b => b.Label)
            .Should().Equal("0-45", "46-90", "91-135", "136+");
        AgingBuckets.Parse("15").Select(b => b.Label)
            .Should().Equal("0-15", "16+");
        // Malformed (descending / junk / empty) → the standard ladder, never an exception.
        AgingBuckets.Parse("90,30").Select(b => b.Label).Should().Equal("0-30", "31-60", "61-90", "91+");
        AgingBuckets.Parse("abc").Select(b => b.Label).Should().Equal("0-30", "31-60", "61-90", "91+");
        AgingBuckets.Parse(null).Select(b => b.Label).Should().Equal("0-30", "31-60", "61-90", "91+");
    }
}
