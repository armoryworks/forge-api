using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Accounting.Qbo;
using Forge.Core.Models.Accounting;

namespace Forge.Api.Controllers;

/// <summary>
/// QB-001 — the config-gated QuickBooks Online journal-summary push surface
/// (§10 ratification 2026-06-12). QuickBooks is NEVER the system of record:
/// this controller only maps accounts and pushes ONE balanced summary JE per
/// period downstream for the CPA — nothing syncs back. Gated on the new
/// <c>CAP-ACCT-QBO-EXPORT</c> capability (default OFF; depends on
/// CAP-ACCT-FULLGL, deliberately outside the BUILTIN⊥EXTERNAL mutex) and the
/// same <c>Controller</c> role as the rest of the GL surface.
/// </summary>
[ApiController]
[Route("api/v1/accounting/qbo-export")]
[Authorize(Roles = "Controller")]
[RequiresCapability("CAP-ACCT-QBO-EXPORT")]
public class QboExportController(IMediator mediator) : ControllerBase
{
    /// <summary>The mapping editor's rows: every postable GL account of the book + its QBO mapping (null = unmapped).</summary>
    [HttpGet("mappings")]
    public async Task<ActionResult<IReadOnlyList<QboAccountMappingModel>>> GetMappings(
        [FromQuery] int bookId, CancellationToken ct)
        => Ok(await mediator.Send(new GetQboAccountMappingsQuery(bookId), ct));

    /// <summary>Upsert the QBO mapping for one GL account (unique per account).</summary>
    [HttpPut("mappings/{glAccountId:int}")]
    public async Task<ActionResult<QboAccountMappingModel>> UpsertMapping(
        int glAccountId, [FromBody] UpsertQboMappingRequest body, CancellationToken ct)
        => Ok(await mediator.Send(
            new UpsertQboAccountMapCommand(glAccountId, body.QboAccountId, body.QboAccountName), ct));

    /// <summary>Remove a GL→QBO mapping (soft delete; the account can be re-mapped later).</summary>
    [HttpDelete("mappings/{glAccountId:int}")]
    public async Task<IActionResult> DeleteMapping(int glAccountId, CancellationToken ct)
    {
        await mediator.Send(new DeleteQboAccountMapCommand(glAccountId), ct);
        return NoContent();
    }

    /// <summary>
    /// Push the period's per-account net as ONE balanced QBO JournalEntry. 409 when any nonzero-net
    /// account is unmapped, or when the range overlaps a prior push and <paramref name="force"/> is
    /// not set (idempotent-by-warning).
    /// </summary>
    [HttpPost("push")]
    public async Task<ActionResult<QboPushResultModel>> Push(
        [FromQuery] int bookId,
        [FromQuery] DateOnly fromDate,
        [FromQuery] DateOnly toDate,
        [FromQuery] bool force,
        CancellationToken ct)
        => Ok(await mediator.Send(new PushQboJournalSummaryCommand(bookId, fromDate, toDate, force), ct));
}

/// <summary>Body for <c>PUT /api/v1/accounting/qbo-export/mappings/{glAccountId}</c>.</summary>
public record UpsertQboMappingRequest(string QboAccountId, string? QboAccountName);
