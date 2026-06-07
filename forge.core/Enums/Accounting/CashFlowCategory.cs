namespace Forge.Core.Enums.Accounting;

/// <summary>
/// Where an account's period change lands on the indirect cash-flow statement. Optional on
/// <c>GlAccount</c>: when unset, the cash-flow statement falls back to a type-based heuristic (non-cash
/// Asset/Liability → Operating, Equity → Financing). Tag long-term-asset / long-term-debt accounts as
/// Investing / Financing once those accounts exist (Phase 4+).
/// </summary>
public enum CashFlowCategory
{
    Operating,
    Investing,
    Financing,
}
