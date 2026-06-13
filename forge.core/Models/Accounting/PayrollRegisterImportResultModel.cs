namespace Forge.Core.Models.Accounting;

/// <summary>⚡ PAY-001 — outcome of one payroll register import (Draft pay run + per-employee lines).</summary>
public record PayrollRegisterImportResultModel(
    int PayRunId,
    int EmployeeCount,
    decimal GrossWages,
    decimal EmployeeTaxWithheld,
    decimal EmployerTax,
    decimal NetPay,
    IReadOnlyList<string> Warnings);
