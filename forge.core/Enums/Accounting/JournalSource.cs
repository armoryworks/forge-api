namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Originating sub-system of a <c>JournalEntry</c>. An <c>IdempotencyKey</c> is
/// required for every non-<see cref="Manual"/> source (incl. Conversion and
/// recurring) — §5.1.
/// </summary>
public enum JournalSource
{
    Manual,
    AR,
    AP,
    Inventory,
    Payroll,
    FX,
    Depreciation,
    Conversion,
    System,
}
