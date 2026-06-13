namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ Phase-5 — a payroll run. The GL foundation: a pay run carries the summarized amounts (gross wages,
/// employee tax withheld, employer tax, net pay) and posts the payroll journal on approval:
/// <list type="bullet">
///   <item><b>Dr</b> WAGE_EXPENSE (gross) + <b>Dr</b> EMPLOYER_PAYROLL_TAX_EXPENSE (employer tax)</item>
///   <item><b>Cr</b> EMPLOYEE_TAX_PAYABLE (withheld) + <b>Cr</b> EMPLOYER_TAX_PAYABLE (employer tax) +
///         <b>Cr</b> NET_PAY_PAYABLE (net) — balances by construction.</item>
/// </list>
/// The tax <i>calculation</i> (gross + tax tables → withheld/employer amounts) is the §8.3 build-vs-integrate
/// spike and is out of this foundation — amounts are provided (external payroll provider or manual entry).
/// </summary>
public class PayRun : BaseAuditableEntity
{
    public int BookId { get; set; }

    public DateOnly PayDate { get; set; }
    public DateOnly PeriodStart { get; set; }
    public DateOnly PeriodEnd { get; set; }

    public decimal GrossWages { get; set; }
    public decimal EmployeeTaxWithheld { get; set; }
    public decimal EmployerTax { get; set; }

    /// <summary>Net pay = gross − employee tax withheld (the cash going to employees).</summary>
    public decimal NetPay => GrossWages - EmployeeTaxWithheld;

    public PayRunStatus Status { get; set; } = PayRunStatus.Draft;

    /// <summary>The posted payroll journal entry (set on approval).</summary>
    public long? JournalEntryId { get; set; }

    /// <summary>Per-employee register rows (PAY-001 import); the run's totals are Σ of these.</summary>
    public ICollection<PayRunLine> Lines { get; set; } = [];
}

/// <summary>Lifecycle of a pay run.</summary>
public enum PayRunStatus
{
    Draft,
    Posted,
}
