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
}

/// <inheritdoc />
public sealed class PayrollRegisterImportService(
    AppDbContext db,
    ISettingsService settings) : IPayrollRegisterImportService
{
    public async Task<PayrollRegisterImportResultModel> ImportAsync(
        int bookId, DateOnly payDate, DateOnly periodStart, DateOnly periodEnd,
        string fileContents, int userId, CancellationToken ct = default)
    {
        var overrides = new PayrollRegisterParser.ColumnOverrides(
            Employee: await settings.GetStringAsync(PayrollSettings.EmployeeColumnKey, ct),
            Gross: await settings.GetStringAsync(PayrollSettings.GrossColumnKey, ct),
            Federal: await settings.GetStringAsync(PayrollSettings.FederalColumnKey, ct),
            State: await settings.GetStringAsync(PayrollSettings.StateColumnKey, ct),
            FicaEmployee: await settings.GetStringAsync(PayrollSettings.FicaEmployeeColumnKey, ct),
            OtherDeductions: await settings.GetStringAsync(PayrollSettings.OtherDeductionsColumnKey, ct),
            EmployerTax: await settings.GetStringAsync(PayrollSettings.EmployerTaxColumnKey, ct),
            Net: await settings.GetStringAsync(PayrollSettings.NetColumnKey, ct));

        var rows = PayrollRegisterParser.Parse(fileContents, overrides);
        if (rows.Count == 0)
            throw new InvalidOperationException("No employee rows found in the register.");

        var warnings = new List<string>();
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
            if (row.Net != 0m && Math.Abs(computedNet - row.Net) > 0.05m)
            {
                warnings.Add(
                    $"{row.EmployeeName}: gross {row.Gross:C} − withholdings {line.TotalWithholdings:C} "
                    + $"= {computedNet:C}, but the register reports net {row.Net:C} — check the column mapping.");
            }
        }

        payRun.GrossWages = payRun.Lines.Sum(l => l.GrossPay);
        payRun.EmployeeTaxWithheld = payRun.Lines.Sum(l => l.TotalWithholdings);
        payRun.EmployerTax = payRun.Lines.Sum(l => l.EmployerTax);

        db.Set<PayRun>().Add(payRun);
        await db.SaveChangesAsync(ct);

        db.LogActivityAt(
            "payroll-register-imported",
            $"Pay run imported from register — {payRun.Lines.Count} employee(s), gross {payRun.GrossWages:C}, "
            + $"net {payRun.NetPay:C}" + (warnings.Count > 0 ? $" ({warnings.Count} warning(s))" : string.Empty),
            ("PayRun", payRun.Id));
        await db.SaveChangesAsync(ct);

        Log.Information(
            "Payroll register imported: pay run {PayRunId}, {Lines} employees, gross {Gross}, {Warnings} warning(s).",
            payRun.Id, payRun.Lines.Count, payRun.GrossWages, warnings.Count);

        return new PayrollRegisterImportResultModel(
            payRun.Id, payRun.Lines.Count, payRun.GrossWages, payRun.EmployeeTaxWithheld,
            payRun.EmployerTax, payRun.NetPay, warnings);
    }
}
