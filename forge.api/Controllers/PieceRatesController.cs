using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.PieceRates;
using Forge.Core.Models.PieceRates;

namespace Forge.Api.Controllers;

/// <summary>
/// Piece rates as effective-dated timelines, piece-work capture at the rate in
/// force on the work date, and the weekly minimum-wage make-up report.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-HR-PIECE-RATES")]
[Route("api/v1/piece-rates")]
public class PieceRatesController(IMediator mediator) : ControllerBase
{
    /// <summary>Every rate scope with its current rate + full timeline.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PieceRateTimelineModel>>> List(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListPieceRatesQuery(), cancellationToken));

    /// <summary>Sets a rate from an effective date (closes the open timeline row).</summary>
    [HttpPost]
    public async Task<ActionResult<PieceRateModel>> Set(
        [FromBody] SetPieceRateCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Piece-work entries in a range, optionally for one worker.</summary>
    [HttpGet("work")]
    public async Task<ActionResult<IReadOnlyList<PieceWorkEntryModel>>> ListWork(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] int? userId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListPieceWorkQuery(from, to, userId), cancellationToken));

    /// <summary>Logs pieces completed on a date at the rate in force that day.</summary>
    [HttpPost("work")]
    public async Task<ActionResult<PieceWorkEntryModel>> LogWork(
        [FromBody] LogPieceWorkCommand command, CancellationToken cancellationToken)
        => Ok(await mediator.Send(command, cancellationToken));

    /// <summary>Soft-deletes a mis-keyed entry.</summary>
    [HttpDelete("work/{id:int}")]
    public async Task<IActionResult> DeleteWork(int id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeletePieceWorkEntryCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>The weekly make-up report: piece earnings vs hours × jurisdiction minimum wage.</summary>
    [HttpGet("compliance")]
    public async Task<ActionResult<PieceRateComplianceModel>> Compliance(
        [FromQuery] DateOnly weekStart, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new GetPieceRateComplianceQuery(weekStart), cancellationToken));
}
