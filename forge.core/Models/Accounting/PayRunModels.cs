using Forge.Core.Entities.Accounting;

namespace Forge.Core.Models.Accounting;

/// <summary>Create a pay run (amounts provided — tax calc is the §8.3 spike, out of this foundation).</summary>
public sealed record CreatePayRunModel(
    int BookId,
    DateOnly PayDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrossWages,
    decimal EmployeeTaxWithheld,
    decimal EmployerTax);

/// <summary>A pay run + derived net pay + the posted journal link.</summary>
public sealed record PayRunModel(
    int Id,
    int BookId,
    DateOnly PayDate,
    DateOnly PeriodStart,
    DateOnly PeriodEnd,
    decimal GrossWages,
    decimal EmployeeTaxWithheld,
    decimal EmployerTax,
    decimal NetPay,
    PayRunStatus Status,
    long? JournalEntryId);
