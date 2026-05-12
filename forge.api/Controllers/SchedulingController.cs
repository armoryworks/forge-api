using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Scheduling;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/scheduling")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-PLAN-CAPACITY")]
public class SchedulingController(IMediator mediator) : ControllerBase
{
    // === Schedule Runs ===

    [HttpPost("run")]
    public async Task<ActionResult<ScheduleRunResponseModel>> RunScheduler([FromBody] RunSchedulerRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var result = await mediator.Send(new RunSchedulerCommand(
            request.Direction, request.ScheduleFrom, request.ScheduleTo,
            request.JobIdFilter, request.PriorityRule, userId));
        return Ok(result);
    }

    [HttpPost("simulate")]
    public async Task<ActionResult<ScheduleRunResponseModel>> SimulateSchedule([FromBody] RunSchedulerRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var result = await mediator.Send(new SimulateScheduleCommand(
            request.Direction, request.ScheduleFrom, request.ScheduleTo,
            request.JobIdFilter, request.PriorityRule, userId));
        return Ok(result);
    }

    [HttpGet("runs")]
    public async Task<ActionResult<List<ScheduleRunResponseModel>>> GetRuns()
    {
        var result = await mediator.Send(new GetScheduleRunsQuery());
        return Ok(result);
    }

    // === Gantt / Operations ===

    [HttpGet("gantt")]
    public async Task<ActionResult<List<ScheduledOperationResponseModel>>> GetGanttData(
        [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var result = await mediator.Send(new GetGanttDataQuery(from, to));
        return Ok(result);
    }

    [HttpPatch("operations/{id:int}")]
    public async Task<IActionResult> RescheduleOperation(int id, [FromBody] RescheduleRequest request)
    {
        await mediator.Send(new RescheduleOperationCommand(id, request.NewStart));
        return NoContent();
    }

    [HttpPost("operations/{id:int}/lock")]
    public async Task<IActionResult> LockOperation(int id, [FromBody] LockRequest request)
    {
        await mediator.Send(new LockScheduledOperationCommand(id, request.IsLocked));
        return NoContent();
    }

    // === Dispatch List ===

    [HttpGet("dispatch/{workCenterId:int}")]
    public async Task<ActionResult<IReadOnlyList<DispatchListItemModel>>> GetDispatchList(int workCenterId)
    {
        var result = await mediator.Send(new GetDispatchListQuery(workCenterId));
        return Ok(result);
    }

    // === Work Center Load ===

    [HttpGet("work-center-load/{workCenterId:int}")]
    public async Task<ActionResult<WorkCenterLoadResponseModel>> GetWorkCenterLoad(
        int workCenterId, [FromQuery] DateOnly from, [FromQuery] DateOnly to)
    {
        var result = await mediator.Send(new GetWorkCenterLoadQuery(workCenterId, from, to));
        return Ok(result);
    }
}

// === Request Models ===

public record RunSchedulerRequest(
    ScheduleDirection Direction,
    DateOnly ScheduleFrom,
    DateOnly ScheduleTo,
    int[]? JobIdFilter,
    string PriorityRule);

public record RescheduleRequest(DateTimeOffset NewStart);

public record LockRequest(bool IsLocked);
