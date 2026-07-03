using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Watchtower;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// regulatory-watchtower (cluster B). Monitored sources + proposed regulatory changes.
/// Admin/Manager only. Polling requires outbound internet (not for air-gapped installs).
/// </summary>
[ApiController]
[Route("api/v1/watchtower")]
[Authorize(Roles = "Admin,Manager")]
public class WatchtowerController(IMediator mediator, IRegulatoryPoller poller) : ControllerBase
{
    [HttpGet("sources")]
    public async Task<ActionResult<List<RegulatorySourceResponseModel>>> GetSources()
        => Ok(await mediator.Send(new GetRegulatorySourcesQuery()));

    [HttpGet("proposals")]
    public async Task<ActionResult<List<RegulatoryProposalResponseModel>>> GetProposals([FromQuery] string? status)
        => Ok(await mediator.Send(new GetRegulatoryProposalsQuery(status)));

    [HttpPost("proposals/{id:int}/apply")]
    public async Task<IActionResult> Apply(int id, [FromBody] ApplyRegulatoryProposalRequestModel? request)
    {
        await mediator.Send(new ReviewRegulatoryProposalCommand(
            id, Apply: true, request?.DueDate, request?.TargetEventTypeId));
        return NoContent();
    }

    [HttpPost("proposals/{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        await mediator.Send(new ReviewRegulatoryProposalCommand(id, Apply: false));
        return NoContent();
    }

    /// <summary>Manually trigger a poll of active sources (also runs on a schedule).</summary>
    [HttpPost("poll")]
    public async Task<ActionResult<object>> Poll(CancellationToken ct)
        => Ok(new { created = await poller.PollActiveAsync(ct) });
}
