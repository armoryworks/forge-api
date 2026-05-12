using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Api.Features.Leads;
using Forge.Api.Features.Leads.BulkIntake;
using Forge.Api.Features.Leads.Queue;
using Forge.Api.Features.OutreachPreferences;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/leads")]
[Authorize(Roles = "Admin,Manager,PM")]
[RequiresCapability("CAP-O2C-LEAD")]
public class LeadsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LeadResponseModel>>> GetLeads(
        [FromQuery] LeadStatus? status,
        [FromQuery] string? search)
    {
        var result = await mediator.Send(new GetLeadsQuery(status, search));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<LeadResponseModel>> GetLeadById(int id)
    {
        var result = await mediator.Send(new GetLeadByIdQuery(id));
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<LeadResponseModel>> CreateLead([FromBody] CreateLeadRequestModel request)
    {
        var result = await mediator.Send(new CreateLeadCommand(request));
        return Created($"/api/v1/leads/{result.Id}", result);
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<LeadResponseModel>> UpdateLead(int id, [FromBody] UpdateLeadRequestModel request)
    {
        var result = await mediator.Send(new UpdateLeadCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/convert")]
    public async Task<ActionResult<ConvertLeadResponseModel>> ConvertLead(int id, [FromBody] ConvertLeadRequestModel request)
    {
        var result = await mediator.Send(new ConvertLeadCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteLead(int id)
    {
        await mediator.Send(new DeleteLeadCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetLeadActivity(int id)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Lead", id));
        return Ok(result);
    }

    /// <summary>
    /// Phase 1r — outreach-preference sidecar reads. Returns 200 with the
    /// preferences row, or 200 + null body when no row exists. Treating
    /// "no row" as 404 would be misleading: the absence is the default
    /// state for most leads, not an error.
    /// </summary>
    [HttpGet("{id:int}/outreach-preferences")]
    public async Task<ActionResult<OutreachPreferencesResponseModel?>> GetOutreachPreferences(int id)
    {
        var result = await mediator.Send(new GetLeadOutreachPreferencesQuery(id));
        return Ok(result);
    }

    /// <summary>
    /// Phase 1r — list leads with active suppression (any channel opt-out
    /// or future cooldown) for the bulk DNC-management UI.
    /// </summary>
    [HttpGet("suppression")]
    public async Task<ActionResult<List<SuppressedLeadSummaryModel>>> ListSuppressed()
        => Ok(await mediator.Send(new ListSuppressedLeadsQuery()));

    [HttpPut("{id:int}/outreach-preferences")]
    public async Task<ActionResult<OutreachPreferencesResponseModel>> UpdateOutreachPreferences(
        int id, [FromBody] UpdateOutreachPreferencesRequest request)
    {
        var result = await mediator.Send(new UpdateLeadOutreachPreferencesCommand(id, request));
        return Ok(result);
    }

    /// <summary>
    /// Phase 1r / Batch 4 — bulk intake preview. Runs the full dedup +
    /// suppression + quality pipeline on the inbound rows but does NOT
    /// persist anything. UI uses this to render the per-row status table
    /// before the operator clicks "import N rows" on the commit endpoint.
    /// </summary>
    [HttpPost("bulk-intake/preview")]
    public async Task<ActionResult<BulkLeadIntakeResponseModel>> BulkIntakePreview(
        [FromBody] BulkLeadIntakeRequest request)
    {
        var result = await mediator.Send(new BulkLeadIntakeCommand(request, Commit: false));
        return Ok(result);
    }

    [HttpPost("bulk-intake/commit")]
    public async Task<ActionResult<BulkLeadIntakeResponseModel>> BulkIntakeCommit(
        [FromBody] BulkLeadIntakeRequest request)
    {
        var result = await mediator.Send(new BulkLeadIntakeCommand(request, Commit: true));
        return Ok(result);
    }

    /// <summary>
    /// Phase 1r / Batch 6 — pull next N leads off the worker queue.
    /// Postgres FOR UPDATE SKIP LOCKED prevents two reps from getting
    /// the same lead. Returned leads have OutreachState flipped to
    /// InProgress; subsequent disposition POSTs advance from there.
    /// </summary>
    [HttpPost("queue/pull")]
    public async Task<ActionResult<List<QueueLeadResponseModel>>> PullQueue([FromBody] PullQueueRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        var result = await mediator.Send(new PullQueueCommand(userId, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/queue/disposition")]
    public async Task<IActionResult> DispositionLead(int id, [FromBody] DispositionLeadRequest request)
    {
        await mediator.Send(new DispositionLeadCommand(id, request));
        return NoContent();
    }
}
