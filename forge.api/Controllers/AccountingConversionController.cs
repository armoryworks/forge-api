using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Controllers;

/// <summary>
/// §7A conversion / cutover HTTP surface (PS-run flow, decision 2026-07-07). Gated behind
/// <c>CAP-ACCT-MIGRATION</c> — deliberately NOT <c>CAP-ACCT-FULLGL</c>, because the opening journal is
/// the prerequisite the FULLGL enable-gate checks. Cutover sequence: enable MIGRATION → POST the
/// opening journal (from the CSV template) → GET the tie-out and compare against the legacy closing
/// TB → enable FULLGL (the wired §7A gate now passes) → optionally disable MIGRATION.
/// </summary>
[ApiController]
[Route("api/v1/accounting/conversion")]
[Authorize(Roles = "Controller")]
[RequiresCapability("CAP-ACCT-MIGRATION")]
public class AccountingConversionController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Posts the book's opening journal — one balanced <c>Source=Conversion</c> entry from the cutover
    /// CSV template rows. Idempotent per book: re-posting returns the already-posted entry.
    /// </summary>
    [HttpPost("opening-journal")]
    public async Task<ActionResult<ManualJournalEntryResult>> ImportOpeningJournal(
        [FromBody] ImportOpeningJournalCommand command, CancellationToken ct)
        => Ok(await mediator.Send(command, ct));

    /// <summary>
    /// §7A go-live tie-out: the native trial balance, readable while FULLGL is still OFF, so the
    /// operator can prove native opening TB == legacy closing TB before flipping the capability.
    /// </summary>
    [HttpGet("tie-out")]
    public async Task<ActionResult<TrialBalance>> GetTieOut(
        [FromQuery] int bookId, [FromQuery] DateOnly? asOfDate, CancellationToken ct)
        => Ok(await mediator.Send(new GetConversionTieOutQuery(bookId, asOfDate), ct));
}
