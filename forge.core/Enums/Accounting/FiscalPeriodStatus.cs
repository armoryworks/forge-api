namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Lifecycle of a <c>FiscalPeriod</c>. Posting is allowed into Open; blocked
/// into SoftClosed unless an audited controller override is supplied; and
/// rejected outright into HardClosed (§5.2).
/// </summary>
public enum FiscalPeriodStatus
{
    Open,
    SoftClosed,
    HardClosed,
}
