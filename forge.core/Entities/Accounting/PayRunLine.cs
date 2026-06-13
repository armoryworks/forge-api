namespace Forge.Core.Entities.Accounting;

/// <summary>
/// ⚡ PAY-001 — one employee's row from the payroll provider's register (owner-ratified
/// per-employee granularity, 2026-06-13). The pay run's posted totals are Σ of these lines,
/// so the journal stays auditable down to the person. "Withholdings" is everything withheld
/// from the employee (taxes + deductions) — net = gross − withholdings by construction.
/// </summary>
public class PayRunLine : BaseEntity
{
    public int PayRunId { get; set; }

    /// <summary>The provider's employee display name (no FK — providers key people their own way).</summary>
    public string EmployeeName { get; set; } = string.Empty;

    public decimal GrossPay { get; set; }

    /// <summary>Federal income tax withheld.</summary>
    public decimal FederalWithholding { get; set; }

    /// <summary>State/local income tax withheld.</summary>
    public decimal StateWithholding { get; set; }

    /// <summary>Employee-side FICA (Social Security + Medicare; split columns sum here).</summary>
    public decimal FicaEmployee { get; set; }

    /// <summary>Non-tax withholdings (401k, insurance, garnishments…).</summary>
    public decimal OtherDeductions { get; set; }

    /// <summary>Employer-side taxes (employer FICA + FUTA + SUTA as the register reports them).</summary>
    public decimal EmployerTax { get; set; }

    public decimal NetPay { get; set; }

    public decimal TotalWithholdings => FederalWithholding + StateWithholding + FicaEmployee + OtherDeductions;

    public PayRun PayRun { get; set; } = null!;
}
