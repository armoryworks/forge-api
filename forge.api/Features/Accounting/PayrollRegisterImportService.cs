using Forge.Api.Services;
using Forge.Core.Entities.Accounting;
using Forge.Core.Models.Accounting;
using Forge.Core.Settings;
using Forge.Data.Context;
using Forge.Data.Extensions;
using Serilog;

namespace Forge.Api.Features.Accounting;

/// <summary>
/// ⚡ PAY-001 — imports a payroll provider's per-employee register CSV as a DRAFT
/// <see cref="PayRun"/> with <see cref="PayRunLine"/>s. Totals are Σ of the lines
/// (GrossWages = Σ gross; EmployeeTaxWithheld = Σ ALL employee-side withholdings so the
/// run's NetPay identity holds; EmployerTax = Σ employer taxes). Lines whose
/// gross − withholdings ≠ reported net (±0.05 rounding) are flagged as warnings — the run
/// still imports as Draft for human review; posting stays the existing PostPayRun step.
/// </summary>
public interface IPayrollRegisterImportService
{
    Task<PayrollRegisterImportResultModel> ImportAsync(
        int bookId, DateOnly payDate, DateOnly periodStart, DateOnly periodEnd,
        string fileContents, int userId, CancellationToken ct = default);

    Task<PayrollRegisterPreviewModel> PreviewAsync(string fileContents, CancellationToken ct = default);
}

/// <inheritdoc />
public sealed class PayrollRegisterImportService(
    AppDbContext db,
    ISettingsService settings) : IPayrollRegisterImportService
{
    public const string MissingRequiredColumns = "missing-required-columns";
    public const string NoEmployeeRows = "no-employee-rows";

    private const decimal NetTolerance = 0.05m;

    public async Task<PayrollRegisterImportResultModel> ImportAsync(
        int bookId, DateOnly payDate, DateOnly periodStart, DateOnly periodEnd,
        string fileContents, int userId, CancellationToken ct = default)
    {
        var overrides = await OverridesAsync(ct);

        var rows = PayrollRegisterParser.Parse(fileContents, overrides);
        if (rows.Count == 0)
            throw new InvalidOperationException("No employee rows found in the register.");

        var warnings = new List<string>();
        var money = await MoneyFormat.ForBaseCurrencyAsync(db, ct);
        var payRun = new PayRun
        {
            BookId = bookId,
            PayDate = payDate,
            PeriodStart = periodStart,
            PeriodEnd = periodEnd,
            Status = PayRunStatus.Draft,
        };

        foreach (var row in rows)
        {
            var line = new PayRunLine
            {
                EmployeeName = row.EmployeeName,
                GrossPay = row.Gross,
                FederalWithholding = row.Federal,
                StateWithholding = row.State,
                FicaEmployee = row.FicaEmployee,
                OtherDeductions = row.OtherDeductions,
                EmployerTax = row.EmployerTax,
                NetPay = row.Net,
            };
            payRun.Lines.Add(line);

            // Net identity check (rounding-tolerant): gross − withholdings should equal reported net.
            var computedNet = row.Gross - line.TotalWithholdings;
            if (IsNetMismatch(row.Net, computedNet))
            {
                warnings.Add(
                    $"{row.EmployeeName}: gross {money.Format(row.Gross)} − withholdings {money.Format(line.TotalWithholdings)} "
                    + $"= {money.Format(computedNet)}, but the register reports net {money.Format(row.Net)} — check the column mapping.");
            }
        }

        payRun.GrossWages = payRun.Lines.Sum(l => l.GrossPay);
        payRun.EmployeeTaxWithheld = payRun.Lines.Sum(l => l.TotalWithholdings);
        payRun.EmployerTax = payRun.Lines.Sum(l => l.EmployerTax);

        db.Set<PayRun>().Add(payRun);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            "payroll-register-imported",
            $"Pay run imported from register — {payRun.Lines.Count} employee(s), gross {money.Format(payRun.GrossWages)}, "
            + $"net {money.Format(payRun.NetPay)}" + (warnings.Count > 0 ? $" ({warnings.Count} warning(s))" : string.Empty),
            ("PayRun", payRun.Id));
        await db.SaveChangesAsync(ct);

        Log.Information(
            "Payroll register imported: pay run {PayRunId}, {Lines} employees, gross {Gross}, {Warnings} warning(s).",
            payRun.Id, payRun.Lines.Count, payRun.GrossWages, warnings.Count);

        return new PayrollRegisterImportResultModel(
            payRun.Id, payRun.Lines.Count, payRun.GrossWages, payRun.EmployeeTaxWithheld,
            payRun.EmployerTax, payRun.NetPay, warnings);
    }

    public async Task<PayrollRegisterPreviewModel> PreviewAsync(string fileContents, CancellationToken ct = default)
    {
        var overrides = await OverridesAsync(ct);
        var columns = PayrollRegisterParser.DescribeColumns(fileContents, overrides);
        var hasRequired = columns.TryGetValue("employee", out var employee) && employee.Count > 0
            && columns.TryGetValue("gross", out var gross) && gross.Count > 0;
        if (!hasRequired)
            return new PayrollRegisterPreviewModel(columns, [], 0m, 0m, 0m, 0m, [MissingRequiredColumns]);

        var rows = PayrollRegisterParser.Parse(fileContents, overrides)
            .Select(r =>
            {
                var withholdings = r.Federal + r.State + r.FicaEmployee + r.OtherDeductions;
                var computedNet = r.Gross - withholdings;
                return new PayrollRegisterPreviewRowModel(
                    r.EmployeeName, r.Gross, withholdings, r.EmployerTax, r.Net, computedNet, IsNetMismatch(r.Net, computedNet));
            })
            .ToList();

        var grossWages = rows.Sum(r => r.Gross);
        var withheld = rows.Sum(r => r.Withholdings);
        return new PayrollRegisterPreviewModel(
            columns,
            rows,
            grossWages,
            withheld,
            rows.Sum(r => r.EmployerTax),
            grossWages - withheld,
            rows.Count == 0 ? [NoEmployeeRows] : []);
    }

    private static bool IsNetMismatch(decimal reportedNet, decimal computedNet)
        => reportedNet != 0m && Math.Abs(computedNet - reportedNet) > NetTolerance;

    private async Task<PayrollRegisterParser.ColumnOverrides> OverridesAsync(CancellationToken ct)
        => new(
            Employee: await settings.GetStringAsync(PayrollSettings.EmployeeColumnKey, ct),
            Gross: await settings.GetStringAsync(PayrollSettings.GrossColumnKey, ct),
            Federal: await settings.GetStringAsync(PayrollSettings.FederalColumnKey, ct),
            State: await settings.GetStringAsync(PayrollSettings.StateColumnKey, ct),
            FicaEmployee: await settings.GetStringAsync(PayrollSettings.FicaEmployeeColumnKey, ct),
            OtherDeductions: await settings.GetStringAsync(PayrollSettings.OtherDeductionsColumnKey, ct),
            EmployerTax: await settings.GetStringAsync(PayrollSettings.EmployerTaxColumnKey, ct),
            Net: await settings.GetStringAsync(PayrollSettings.NetColumnKey, ct));
}
