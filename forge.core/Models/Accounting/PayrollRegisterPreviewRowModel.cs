namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ PAY-001 — one employee row of a register preview: gross, total withholdings, employer tax, the net the
/// register reports and the net Forge computes; <see cref="NetMismatch"/> flags a gap over the rounding tolerance.
/// </summary>
public record PayrollRegisterPreviewRowModel(
    string EmployeeName,
    decimal Gross,
    decimal Withholdings,
    decimal EmployerTax,
    decimal ReportedNet,
    decimal ComputedNet,
    bool NetMismatch);
