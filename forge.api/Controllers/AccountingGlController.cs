using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
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
