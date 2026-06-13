namespace Forge.Core.Models.Accounting;

/// <summary>
/// ⚡ Phase-3 — indirect-method Cash-Flow statement. Starts from net income and reconciles to the net change
/// in cash by the working-capital changes over the window, grouped into Operating / Investing / Financing.
/// <para>Built entirely from the ledger by the double-entry identity: the change in the cash account(s)
/// equals the negative of the change in every other account, so <see cref="NetChangeInCash"/> reconciles to
/// the actual cash-account movement (<see cref="ActualCashChange"/>) by construction.</para>
/// <para><b>Classification (ratify):</b> until accounts carry an explicit cash-flow tag, the split is by
/// account type — non-cash <b>Asset</b> + <b>Liability</b> changes are Operating (working capital), <b>Equity</b>
/// changes (excluding the year-end income roll, which is already in net income) are Financing, and Investing
/// is empty (there are no long-term-asset accounts yet; tag them when fixed assets / long-term debt arrive in
/// Phase 4+).</para>
/// </summary>
public sealed class CashFlowStatement
{
    public int BookId { get; init; }
    public DateOnly? FromDate { get; init; }
    public DateOnly ToDate { get; init; }

    /// <summary>Net income for the window (the indirect-method starting line).</summary>
    public decimal NetIncome { get; init; }

    /// <summary>Working-capital adjustments (Δ of operating non-cash balance-sheet accounts, cash-flow signed).</summary>
    public IReadOnlyList<CashFlowLine> OperatingAdjustments { get; init; } = [];
    public decimal NetCashFromOperating { get; init; }

    public IReadOnlyList<CashFlowLine> Investing { get; init; } = [];
    public decimal NetCashFromInvesting { get; init; }

    public IReadOnlyList<CashFlowLine> Financing { get; init; } = [];
    public decimal NetCashFromFinancing { get; init; }

    /// <summary>Operating + Investing + Financing — the cash-flow-derived change in cash.</summary>
    public decimal NetChangeInCash { get; init; }

    /// <summary>The actual movement of the cash account(s) over the window (the reconciliation target).</summary>
    public decimal ActualCashChange { get; init; }

    public decimal RoundingTolerance { get; init; }
    public bool IsReconciled => Math.Abs(NetChangeInCash - ActualCashChange) <= RoundingTolerance;
}

/// <summary>One account's cash-flow contribution over the window (already cash-flow signed).</summary>
public sealed class CashFlowLine
{
    public int GlAccountId { get; init; }
    public string AccountNumber { get; init; } = string.Empty;
    public string AccountName { get; init; } = string.Empty;
    /// <summary>Positive = a source of cash, negative = a use of cash.</summary>
    public decimal Amount { get; init; }
}
