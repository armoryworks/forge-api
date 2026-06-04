namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Lifecycle of a <c>FiscalYear</c>. A Closed year is fully locked (all its
/// periods HardClosed and the year-end retained-earnings roll posted).
/// </summary>
public enum FiscalYearStatus
{
    Open,
    Closed,
}
