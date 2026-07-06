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
                request.Lines,
                request.ApprovedByUserId),
            ct);

        return CreatedAtAction(
            nameof(GetTrialBalance),
            new { bookId = result.BookId },
            result);
    }

    /// <summary>
    /// Maker-checker async work-list (§5.7): the manual JEs awaiting a second approver
    /// (<c>PendingApproval</c>) for a book — what the create endpoint routes an over-threshold entry to when no
    /// distinct approver was supplied up-front.
    /// </summary>
    [HttpGet("journal-entries/pending")]
    public async Task<ActionResult<IReadOnlyList<ManualJournalEntryResult>>> GetPendingJournalEntries(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPendingJournalEntriesQuery(bookId), ct));

    /// <summary>
    /// Read-only chart of accounts for a book (§5A) — the account pick-list behind the manual
    /// journal-entry editor. <c>postableOnly=true</c> narrows to hand-postable (postable, non-control)
    /// accounts.
    /// </summary>
    [HttpGet("accounts")]
    public async Task<ActionResult<IReadOnlyList<GlAccountModel>>> GetChartOfAccounts(
        [FromQuery] int bookId, [FromQuery] bool postableOnly = false, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetChartOfAccountsQuery(bookId, postableOnly), ct));

    /// <summary>
    /// Read-only GL register (ACCOUNTING_SUITE_PLAN §5A): the time-ordered journal for a book with
    /// per-line account labels and drill-back refs, feeding the ledger-view UI. Newest first,
    /// offset-paginated, optionally filtered by date range, entry status, and account.
    /// </summary>
    [HttpGet("ledger")]
    public async Task<ActionResult<LedgerRegisterPage>> GetLedgerRegister(
        [FromQuery] int bookId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] JournalEntryStatus? status, [FromQuery] int? glAccountId,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default)
        => Ok(await mediator.Send(
            new GetLedgerRegisterQuery(bookId, fromDate, toDate, status, glAccountId, page, pageSize), ct));

    /// <summary>
    /// Read-only Accounting-AI advisory (§5A): a plain-language explanation of a journal entry for a
    /// reviewer. Advisory only — reads the ledger and narrates via the assistant, never posts; degrades
    /// to a deterministic summary when the assistant is offline.
    /// </summary>
    [HttpGet("journal-entries/{entryId:long}/explain")]
    public async Task<ActionResult<JournalEntryExplanation>> ExplainJournalEntry(
        long entryId, [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new ExplainJournalEntryQuery(bookId, entryId), ct));

    /// <summary>
    /// Read-only anomaly scan (§5A): a reviewer's "look at these" list over the book's posted manual
    /// journal entries — deterministic rules (manual posting to a control account; large manual entry
    /// at/above <c>largeManualThreshold</c>). Flagged entries can be narrated via the explain endpoint.
    /// </summary>
    [HttpGet("anomalies")]
    public async Task<ActionResult<IReadOnlyList<GlAnomaly>>> GetGlAnomalies(
        [FromQuery] int bookId, [FromQuery] DateOnly? fromDate, [FromQuery] DateOnly? toDate,
        [FromQuery] decimal largeManualThreshold = 100000m, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetGlAnomaliesQuery(bookId, fromDate, toDate, largeManualThreshold), ct));

    /// <summary>
    /// Maker-checker async approval (§5.7): finalize a <c>PendingApproval</c> manual JE to <c>Posted</c>,
    /// folding it into the ledger. The approver (this caller) must differ from the submitter — the engine
    /// enforces it (<c>APPROVER_NOT_DISTINCT</c>).
    /// </summary>
    [HttpPost("journal-entries/{id:long}/approve")]
    public async Task<ActionResult<ManualJournalEntryResult>> ApproveJournalEntry(long id, CancellationToken ct)
        => Ok(await mediator.Send(new ApproveJournalEntryCommand(id), ct));

    /// <summary>
    /// Maker-checker async rejection (§5.7): return a <c>PendingApproval</c> manual JE to <c>Draft</c> with an
    /// optional reason (appended to the memo). Nothing was applied to the ledger, so nothing unwinds.
    /// </summary>
    [HttpPost("journal-entries/{id:long}/reject")]
    public async Task<ActionResult<ManualJournalEntryResult>> RejectJournalEntry(
        long id, [FromQuery] string? reason, CancellationToken ct)
        => Ok(await mediator.Send(new RejectJournalEntryCommand(id, reason), ct));

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

    /// <summary>Phase-3 — the book's fiscal calendar (years + periods with statuses) for the close screen.</summary>
    [HttpGet("fiscal-calendar")]
    public async Task<ActionResult<IReadOnlyList<FiscalYearModel>>> GetFiscalCalendar(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetFiscalCalendarQuery(bookId), ct));

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

    /// <summary>Phase-3 — the pre-close checklist for a period (GRNI reconciled, AR/AP tie to control). A
    /// hard-close is blocked unless it all passes.</summary>
    [HttpGet("periods/{id:int}/close-checklist")]
    public async Task<ActionResult<CloseChecklistResult>> GetCloseChecklist(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetCloseChecklistQuery(id), ct));

    /// <summary>
    /// Phase-3 — year-end close: posts the Retained-Earnings roll (zeroes every P&amp;L account into RE),
    /// hard-closes every period in the year, and marks the year Closed. CAP-ACCT-FULLGL gated.
    /// </summary>
    [HttpPost("years/{id:int}/close")]
    public async Task<ActionResult<YearEndCloseResult>> CloseFiscalYear(int id, CancellationToken ct)
        => Ok(await mediator.Send(new CloseFiscalYearCommand(id), ct));

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

    /// <summary>
    /// Phase-3 — indirect-method Cash-Flow statement over a window (net income → working-capital changes →
    /// change in cash, reconciled to the actual cash movement). Dual-gated like the P&amp;L / Balance Sheet.
    /// </summary>
    [HttpGet("cash-flow")]
    [RequiresCapability("CAP-RPT-FINANCIALS")]
    public async Task<ActionResult<CashFlowStatement>> GetCashFlow(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetCashFlowStatementQuery(bookId, fromDate, toDate), ct);
        return Ok(result);
    }

    /// <summary>Phase-4b — run a period-end unrealized FX revaluation for a currency (auto-reverses next period).</summary>
    [HttpPost("fx-revaluation")]
    [RequiresCapability("CAP-ACCT-FXREVAL")]
    public async Task<ActionResult<FxRevaluationResult>> RevalueFx(
        [FromBody] RevalueFxRequest body, CancellationToken ct)
        => Ok(await mediator.Send(new RevalueFxCommand(body.BookId, body.CurrencyId, body.NewRate, body.AsOf), ct));

    /// <summary>
    /// Close a job's production cost (STAGE E completion): absorb the job's actual labor + overhead into WIP
    /// and sweep the remaining WIP balance to PRODUCTION_VARIANCE. Idempotent; run after the job's production
    /// receipts.
    /// </summary>
    [HttpPost("jobs/{jobId:int}/close-production-cost")]
    public async Task<ActionResult<JobProductionCostCloseResult>> CloseJobProductionCost(int jobId, CancellationToken ct)
        => Ok(await mediator.Send(new CloseJobProductionCostCommand(jobId), ct));

    /// <summary>Standard costing — record actual overhead incurred into the single-plant pool (OVERHEAD_CONTROL).</summary>
    [HttpPost("overhead/record")]
    public async Task<ActionResult> RecordActualOverhead([FromBody] RecordActualOverheadRequest body, CancellationToken ct)
    {
        await mediator.Send(new RecordActualOverheadCommand(body.Amount, body.Memo ?? string.Empty, body.EntryDate), ct);
        return NoContent();
    }

    /// <summary>Standard costing — close the overhead pool for a period: post the spending variance + clear the pool.</summary>
    [HttpPost("overhead/close")]
    public async Task<ActionResult<OverheadPoolCloseResult>> CloseOverheadPool([FromBody] CloseOverheadPoolRequest body, CancellationToken ct)
        => Ok(await mediator.Send(new CloseOverheadPoolCommand(body.AsOf), ct));

    /// <summary>Standard costing — variance rollup for a date range (the six slots + residual; lumped = sum).</summary>
    [HttpGet("variances")]
    public async Task<ActionResult<VarianceReportModel>> GetVariances(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, CancellationToken ct)
        => Ok(await mediator.Send(new GetVarianceReportQuery(from, to), ct));

    // ─────────────────────────── Phase-5 payroll ───────────────────────────

    /// <summary>Phase-5 — create a pay run (amounts provided; tax calc is the §8.3 spike, out of scope here).</summary>
    [HttpPost("pay-runs")]
    [RequiresCapability("CAP-PAYROLL-RUN")]
    public async Task<ActionResult<PayRunModel>> CreatePayRun([FromBody] CreatePayRunModel model, CancellationToken ct)
        => Ok(await mediator.Send(new CreatePayRunCommand(model), ct));

    /// <summary>
    /// PAY-001 — import the payroll provider's per-employee register CSV as a DRAFT pay run
    /// (column mapping auto-detected; pin exact headers via Admin → Settings → Payroll).
    /// Posting remains the separate PostPayRun step.
    /// </summary>
    [HttpPost("payroll/runs/import")]
    public async Task<ActionResult<PayrollRegisterImportResultModel>> ImportPayrollRegister(
        [FromForm] int bookId, [FromForm] DateOnly payDate,
        [FromForm] DateOnly periodStart, [FromForm] DateOnly periodEnd,
        IFormFile file,
        [FromServices] Forge.Api.Features.Accounting.IPayrollRegisterImportService importService,
        CancellationToken ct)
    {
        using var reader = new StreamReader(file.OpenReadStream());
        var contents = await reader.ReadToEndAsync(ct);
        var userId = int.Parse(
            User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)!.Value);
        return Ok(await importService.ImportAsync(bookId, payDate, periodStart, periodEnd, contents, userId, ct));
    }

    /// <summary>Phase-5 — list a book's pay runs.</summary>
    [HttpGet("pay-runs")]
    [RequiresCapability("CAP-PAYROLL-RUN")]
    public async Task<ActionResult<IReadOnlyList<PayRunModel>>> ListPayRuns([FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new ListPayRunsQuery(bookId), ct));

    /// <summary>Phase-5 — post the payroll journal for a pay run.</summary>
    [HttpPost("pay-runs/{id:int}/post")]
    [RequiresCapability("CAP-PAYROLL-RUN")]
    public async Task<ActionResult<PayRunModel>> PostPayRun(int id, CancellationToken ct)
        => Ok(await mediator.Send(new PostPayRunCommand(id), ct));

    /// <summary>§7A conversion — post the opening-balance journal (balance-sheet opening balances + AR/AP
    /// open items) at go-live. Idempotent per book.</summary>
    [HttpPost("opening-balances")]
    public async Task<ActionResult<OpeningBalanceResult>> PostOpeningBalances(
        [FromBody] PostOpeningBalancesModel model, CancellationToken ct)
        => Ok(await mediator.Send(new PostOpeningBalancesCommand(model), ct));

    // ─────────────────────────── Phase-4 fixed-asset depreciation ───────────────────────────

    /// <summary>Phase-4 — register a depreciable fixed asset.</summary>
    [HttpPost("fixed-assets")]
    [RequiresCapability("CAP-ACCT-DEPRECIATION")]
    public async Task<ActionResult<FixedAssetModel>> CreateFixedAsset(
        [FromBody] CreateFixedAssetModel model, CancellationToken ct)
        => Ok(await mediator.Send(new CreateFixedAssetCommand(model), ct));

    /// <summary>Phase-4 — list a book's fixed assets with derived depreciation.</summary>
    [HttpGet("fixed-assets")]
    [RequiresCapability("CAP-ACCT-DEPRECIATION")]
    public async Task<ActionResult<IReadOnlyList<FixedAssetModel>>> ListFixedAssets(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new ListFixedAssetsQuery(bookId), ct));

    /// <summary>Phase-4 — post a month's depreciation for the book (idempotent per asset per month).</summary>
    [HttpPost("depreciation/run")]
    [RequiresCapability("CAP-ACCT-DEPRECIATION")]
    public async Task<ActionResult<DepreciationRunResult>> RunDepreciation(
        [FromBody] RunDepreciationRequest body, CancellationToken ct)
        => Ok(await mediator.Send(new RunDepreciationCommand(body.BookId, body.PeriodMonth), ct));

    // ─────────────────────────── Phase-2 STAGE E inventory valuation ───────────────────────────

    /// <summary>STAGE E — the perpetual inventory valuation (on-hand qty, avg cost, value) per part.</summary>
    [HttpGet("inventory-valuation")]
    public async Task<ActionResult<IReadOnlyList<InventoryValuationModel>>> GetInventoryValuation(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetInventoryValuationQuery(bookId), ct));

    /// <summary>STAGE E — valuation sub-ledger vs GL inventory-control reconciliation.</summary>
    [HttpGet("inventory-valuation/reconciliation")]
    public async Task<ActionResult<InventoryValuationReconciliation>> GetInventoryValuationReconciliation(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetInventoryValuationReconciliationQuery(bookId), ct));

    // ─────────────────────────── QB-001 CPA CSV exports ───────────────────────────

    /// <summary>
    /// QB-001 — trial balance CSV download for the CPA (§10 ratification: "CSV/Excel export always
    /// available"). Same gating as the rest of the GL surface (Controller role + CAP-ACCT-FULLGL).
    /// </summary>
    [HttpGet("exports/trial-balance.csv")]
    public async Task<IActionResult> ExportTrialBalanceCsv(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ExportTrialBalanceCsvQuery(bookId, fromDate, toDate), ct);
        return File(result.Content, "text/csv", result.FileName);
    }

    /// <summary>
    /// QB-001 — full GL detail CSV download (one row per journal line, Posted + Reversed, ordered by
    /// entry number then line number).
    /// </summary>
    [HttpGet("exports/gl-detail.csv")]
    public async Task<IActionResult> ExportGlDetailCsv(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ExportGlDetailCsvQuery(bookId, fromDate, toDate), ct);
        return File(result.Content, "text/csv", result.FileName);
    }

    /// <summary>
    /// QB-001 — per-account period-net journal summary CSV (the "one monthly JE" the CPA keys into
    /// their system). Same aggregation the config-gated QBO push uses.
    /// </summary>
    [HttpGet("exports/journal-summary.csv")]
    public async Task<IActionResult> ExportJournalSummaryCsv(
        [FromQuery] int bookId,
        [FromQuery] DateOnly? fromDate,
        [FromQuery] DateOnly? toDate,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ExportJournalSummaryCsvQuery(bookId, fromDate, toDate), ct);
        return File(result.Content, "text/csv", result.FileName);
    }

    // ─────────────────────────── Phase-3 journal templates ───────────────────────────

    /// <summary>Phase-3 — create a recurring/standard journal template.</summary>
    [HttpPost("journal-templates")]
    public async Task<ActionResult<JournalTemplateModel>> CreateJournalTemplate(
        [FromBody] CreateJournalTemplateModel model, CancellationToken ct)
        => Ok(await mediator.Send(new CreateJournalTemplateCommand(model), ct));

    /// <summary>Phase-3 — list a book's journal templates.</summary>
    [HttpGet("journal-templates")]
    public async Task<ActionResult<IReadOnlyList<JournalTemplateModel>>> ListJournalTemplates(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new ListJournalTemplatesQuery(bookId), ct));

    /// <summary>Phase-3 — post a journal entry from a template for a given date.</summary>
    [HttpPost("journal-templates/{id:int}/post")]
    public async Task<ActionResult<PostedFromTemplateModel>> PostFromTemplate(
        int id, [FromBody] PostFromTemplateRequest body, CancellationToken ct)
        => Ok(await mediator.Send(new PostFromTemplateCommand(id, body.EntryDate, body.Memo), ct));

    // ─────────────────────────── Phase-3 bank reconciliation ───────────────────────────

    /// <summary>Phase-3 — cash GL accounts available to reconcile.</summary>
    [HttpGet("cash-accounts")]
    public async Task<ActionResult<IReadOnlyList<CashAccountModel>>> GetCashAccounts(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetCashAccountsQuery(bookId), ct));

    /// <summary>Phase-3 — list a book's bank reconciliations.</summary>
    [HttpGet("bank-reconciliations")]
    public async Task<ActionResult<IReadOnlyList<BankReconciliationSummary>>> ListBankReconciliations(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new ListBankReconciliationsQuery(bookId), ct));

    /// <summary>Phase-3 — start a bank reconciliation (Draft) for a cash account against a statement.</summary>
    [HttpPost("bank-reconciliations")]
    public async Task<ActionResult<BankReconciliationWorksheet>> StartBankReconciliation(
        [FromBody] StartBankReconciliationRequest body, CancellationToken ct)
        => Ok(await mediator.Send(new StartBankReconciliationCommand(
            body.BookId, body.CashGlAccountId, body.StatementDate, body.StatementEndingBalance), ct));

    /// <summary>Phase-3 — fetch a reconciliation worksheet.</summary>
    [HttpGet("bank-reconciliations/{id:int}")]
    public async Task<ActionResult<BankReconciliationWorksheet>> GetBankReconciliation(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetBankReconciliationQuery(id), ct));

    /// <summary>Phase-3 — toggle a cash line's cleared flag on a Draft reconciliation.</summary>
    [HttpPost("bank-reconciliations/{id:int}/items/{journalLineId:long}/cleared")]
    public async Task<ActionResult<BankReconciliationWorksheet>> SetBankReconciliationItemCleared(
        int id, long journalLineId, [FromQuery] bool cleared, CancellationToken ct)
        => Ok(await mediator.Send(new SetBankReconciliationItemClearedCommand(id, journalLineId, cleared), ct));

    /// <summary>Phase-3 — finalize a reconciliation (requires it to be in balance).</summary>
    [HttpPost("bank-reconciliations/{id:int}/finalize")]
    public async Task<ActionResult<BankReconciliationWorksheet>> FinalizeBankReconciliation(int id, CancellationToken ct)
        => Ok(await mediator.Send(new FinalizeBankReconciliationCommand(id), ct));
}

/// <summary>Body for <c>POST /api/v1/accounting/bank-reconciliations</c>.</summary>
public record StartBankReconciliationRequest(int BookId, int CashGlAccountId, DateOnly StatementDate, decimal StatementEndingBalance);

/// <summary>Body for <c>POST /api/v1/accounting/journal-templates/{id}/post</c>.</summary>
public record PostFromTemplateRequest(DateOnly EntryDate, string? Memo);

/// <summary>Body for <c>POST /api/v1/accounting/depreciation/run</c>.</summary>
public record RunDepreciationRequest(int BookId, DateOnly PeriodMonth);

/// <summary>Body for <c>POST /api/v1/accounting/fx-revaluation</c>.</summary>
public record RevalueFxRequest(int BookId, int CurrencyId, decimal NewRate, DateOnly AsOf);

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
    IReadOnlyList<CreateManualJournalLineModel> Lines,
    int? ApprovedByUserId = null);
