namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Phase-3 — outcome of a year-end close. The closing entry zeroes every P&amp;L account into Retained
/// Earnings (so the new year starts clean and RE carries the result), then every period in the year is
/// hard-closed and the year is marked Closed.
/// </summary>
public sealed record YearEndCloseResult(
    int FiscalYearId,
    long? JournalEntryId,
    decimal NetIncome,
    int RetainedEarningsAccountId,
    int PeriodsHardClosed);
