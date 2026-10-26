namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ PAY-001 — dry run of a payroll register import: which register headers fed each category (employee,
/// gross, federal, state, ficaEmployee, otherDeductions, employerTax, net), the parsed rows, the run totals
/// and the problem codes that would stop the import (<c>missing-required-columns</c>, <c>no-employee-rows</c>).
/// Nothing is saved.
/// </summary>
public record PayrollRegisterPreviewModel(
    IReadOnlyDictionary<string, IReadOnlyList<string>> Columns,
    IReadOnlyList<PayrollRegisterPreviewRowModel> Rows,
    decimal GrossWages,
    decimal EmployeeTaxWithheld,
    decimal EmployerTax,
    decimal NetPay,
    IReadOnlyList<string> Errors);
