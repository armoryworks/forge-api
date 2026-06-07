using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Enums.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase-0 general-ledger HTTP surface (§5.5 / §5.9 acceptance: "post a manual
/// balanced journal" + "produce a trial balance"). Kept <b>DARK</b>: the whole
/// controller is gated on <c>CAP-ACCT-FULLGL</c> via
/// <see cref="RequiresCapabilityAttribute"/>, which the
/// <c>CapabilityGateMiddleware</c> reads at the controller edge. With that
/// capability OFF (its default — registered "for future delivery, not yet
/// implemented") every action here short-circuits with 403 before the handler
/// runs, so the posting engine and trial-balance read path are unreachable.
/// <para>
/// This is the <b>only</b> runtime reach into the GL engine in Phase 0 — no
/// operational command site (Invoice/PO/Job/…) calls <c>IPostingEngine</c> yet
/// (that is Phase 1, which would un-dark the engine). Authorization mirrors the
/// §5.7 SoD intent: posting reaches the books via the <c>Controller</c> role (or
/// any rollup that composes it — e.g. the seeded <c>OwnerOperator</c>); bare
/// Admin/Manager are off the books.
/// </para>
/// </summary>
[ApiController]
[Route("api/v1/accounting")]
[Authorize(Roles = "Controller")]
[RequiresCapability("CAP-ACCT-FULLGL")]
public class AccountingGlController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Post a manual, balanced double-entry journal (§5.9). The handler builds a
    /// <see cref="PostingRequest"/> and calls <c>IPostingEngine.PostAsync</c>
    /// inline. An unbalanced / invalid request surfaces as a
    /// <c>PostingException</c> → 400 (validation 400 also fires at the edge).
    /// </summary>
    [HttpPost("journal-entries")]
    public async Task<ActionResult<ManualJournalEntryResult>> CreateManualJournalEntry(
        [FromBody] CreateManualJournalEntryRequest request,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new CreateManualJournalEntryCommand(
                request.BookId,
                request.EntryDate,
                request.CurrencyId,
                request.Memo,
                request.AllowSoftClosedOverride,
                request.Lines),
            ct);

        return CreatedAtAction(
            nameof(GetTrialBalance),
            new { bookId = result.BookId },
            result);
    }

    /// <summary>
    /// Produce a filter-immune trial balance for the book over an optional date
    /// range (§5.3 / §5.9). Asserts total Dr == total Cr via
    /// <see cref="TrialBalance.IsBalanced"/>.
    /// </summary>
    [HttpGet("trial-balance")]
    public async Task<ActionResult<TrialBalance>> GetTrialBalance(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetTrialBalanceQuery(bookId, fromDate, toDate), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase-1 STAGE D — produce the AR sub-ledger + aging for the book as of an
    /// optional date (defaults to today). Derived from posted AR-control
    /// <see cref="Forge.Core.Entities.Accounting.JournalLine"/>s carrying a
    /// Customer party, bucketed by age, with an AR-control-vs-aging
    /// reconciliation attached (§6 Phase-1 row "AR sub-ledger + aging").
    /// </summary>
    [HttpGet("ar-aging")]
    public async Task<ActionResult<ArAging>> GetArAging(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? asOfDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetArAgingQuery(bookId, asOfDate), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase-2 STAGE A — Accounts-Payable aging: open payables by vendor and age bucket, with an
    /// AP-control-vs-aging reconciliation (§6 Phase-2 row "AP sub-ledger + aging"). Sub-ledger report
    /// (CAP-ACCT-FULLGL at the edge, like ar-aging — NOT a financial statement, so no CAP-RPT-FINANCIALS).
    /// </summary>
    [HttpGet("ap-aging")]
    public async Task<ActionResult<ApAging>> GetApAging(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? asOfDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetApAgingQuery(bookId, asOfDate), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase-3 — soft-close a fiscal period (Open → SoftClosed). Posting into it then requires an audited
    /// controller override; reopen returns it to Open. CAP-ACCT-FULLGL gated.
    /// </summary>
    [HttpPost("periods/{id:int}/soft-close")]
    public async Task<ActionResult<FiscalPeriodModel>> SoftClosePeriod(int id, CancellationToken ct)
        => Ok(await mediator.Send(new SetFiscalPeriodStatusCommand(id, FiscalPeriodStatus.SoftClosed), ct));

    /// <summary>Phase-3 — hard-close a fiscal period (→ HardClosed): a permanent lock; posting is rejected outright.</summary>
    [HttpPost("periods/{id:int}/hard-close")]
    public async Task<ActionResult<FiscalPeriodModel>> HardClosePeriod(int id, CancellationToken ct)
        => Ok(await mediator.Send(new SetFiscalPeriodStatusCommand(id, FiscalPeriodStatus.HardClosed), ct));

    /// <summary>Phase-3 — reopen a soft-closed fiscal period (SoftClosed → Open). A hard-closed period cannot reopen.</summary>
    [HttpPost("periods/{id:int}/reopen")]
    public async Task<ActionResult<FiscalPeriodModel>> ReopenPeriod(int id, CancellationToken ct)
        => Ok(await mediator.Send(new SetFiscalPeriodStatusCommand(id, FiscalPeriodStatus.Open), ct));

    /// <summary>
    /// Phase-2 STAGE D.3 — GRNI (Goods-Received-Not-Invoiced) reconciliation + aging: open received-not-billed
    /// by purchase order and age bucket, the GL-GRNI-vs-operational variance (§12 control), and a line-level
    /// uncovered-receipt drill-down. Sub-ledger/clearing report (CAP-ACCT-FULLGL at the edge — it only means
    /// anything once GRNI is being posted; not a financial statement, so no CAP-RPT-FINANCIALS).
    /// </summary>
    [HttpGet("grni-reconciliation")]
    public async Task<ActionResult<GrniReconciliation>> GetGrniReconciliation(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? asOfDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetGrniReconciliationQuery(bookId, asOfDate), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase-1 STAGE E — Profit &amp; Loss for the book over an optional period
    /// range (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L + Balance Sheet").
    /// Built over the trial-balance ledger projection restricted to Income/Expense
    /// accounts (<c>GlAccount.AccountType</c>).
    /// <para>
    /// <b>Dual-gated.</b> The method-level <see cref="RequiresCapabilityAttribute"/>
    /// <c>CAP-RPT-FINANCIALS</c> (financial-statements reporting gate, default OFF
    /// until COGS posting is live — §6 / §10) is what the HTTP capability
    /// middleware enforces at the edge (it overrides the controller-level
    /// <c>CAP-ACCT-FULLGL</c> for this action). The query type still carries
    /// <c>CAP-ACCT-FULLGL</c>, so the MediatR <c>CapabilityGateBehavior</c>
    /// enforces the GL engine gate as well — <b>both</b> capabilities must be ON to
    /// reach the handler.
    /// </para>
    /// <para>
    /// <b>Incomplete-margin label:</b> COGS is not posted until Phase 2, so the
    /// result carries <c>CogsPosted = false</c> + a margin caveat; gross margin is
    /// incomplete.
    /// </para>
    /// </summary>
    [HttpGet("pnl")]
    [RequiresCapability("CAP-RPT-FINANCIALS")]
    public async Task<ActionResult<ProfitAndLoss>> GetProfitAndLoss(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetProfitAndLossQuery(bookId, fromDate, toDate), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase-1 STAGE E — Balance Sheet for the book as of an optional date
    /// (defaults to today) (ACCOUNTING_SUITE_PLAN §6 Phase-1 row "P&amp;L + Balance
    /// Sheet"). Built over the trial-balance ledger projection restricted to
    /// Asset/Liability/Equity accounts, plus a computed current-year-earnings
    /// equity line so the sheet balances before the Phase-3 year-end
    /// Retained-Earnings roll.
    /// <para>
    /// <b>Dual-gated</b> identically to <see cref="GetProfitAndLoss"/>:
    /// <c>CAP-RPT-FINANCIALS</c> at the HTTP edge + <c>CAP-ACCT-FULLGL</c> on the
    /// query. Both default OFF.
    /// </para>
    /// <para>
    /// <b>Incomplete-margin label:</b> current-year-earnings inherits the
    /// not-yet-posted-COGS limitation (Phase 2); <c>CogsPosted = false</c> + a
    /// caveat are returned.
    /// </para>
    /// </summary>
    [HttpGet("balance-sheet")]
    [RequiresCapability("CAP-RPT-FINANCIALS")]
    public async Task<ActionResult<BalanceSheet>> GetBalanceSheet(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? asOfDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetBalanceSheetQuery(bookId, asOfDate), ct);
        return Ok(result);
    }
}

/// <summary>
/// Request body for <c>POST /api/v1/accounting/journal-entries</c>. The
/// server-trusted posting principal (PostedBy) is taken from the auth context
/// in the handler — never from the body.
/// </summary>
public record CreateManualJournalEntryRequest(
    int BookId,
    DateOnly EntryDate,
    int CurrencyId,
    string? Memo,
    bool AllowSoftClosedOverride,
    IReadOnlyList<CreateManualJournalLineModel> Lines);
