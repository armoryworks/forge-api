using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.StatusTracking;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/status-tracking")]
[Authorize]
[RequiresCapability("CAP-CROSS-ACTIVITY-LOG")]
public class StatusTrackingController(IMediator mediator) : ControllerBase
{
    [HttpGet("{entityType}/{entityId:int}/history")]
    public async Task<ActionResult<List<StatusEntryResponseModel>>> GetHistory(
        string entityType, int entityId)
    {
        var result = await mediator.Send(new GetStatusHistoryQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpGet("{entityType}/{entityId:int}/active")]
    public async Task<ActionResult<ActiveStatusResponseModel>> GetActiveStatus(
        string entityType, int entityId)
    {
        var result = await mediator.Send(new GetActiveStatusQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpPost("{entityType}/{entityId:int}/workflow")]
    public async Task<ActionResult<StatusEntryResponseModel>> SetWorkflowStatus(
        string entityType, int entityId, [FromBody] SetStatusRequestModel request)
    {
        var result = await mediator.Send(new SetWorkflowStatusCommand(entityType, entityId, request));
        return Ok(result);
    }

    [HttpPost("{entityType}/{entityId:int}/holds")]
    public async Task<ActionResult<StatusEntryResponseModel>> AddHold(
        string entityType, int entityId, [FromBody] AddHoldRequestModel request)
    {
        var result = await mediator.Send(new AddHoldCommand(entityType, entityId, request));
        return StatusCode(201, result);
    }

    [HttpPost("holds/{id:int}/release")]
    public async Task<ActionResult<StatusEntryResponseModel>> ReleaseHold(
        int id, [FromBody] ReleaseHoldRequestModel? request)
    {
        var result = await mediator.Send(new ReleaseHoldCommand(id, request));
        return Ok(result);
    }
}
