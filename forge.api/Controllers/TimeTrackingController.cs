using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.TimeTracking;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/time-tracking")]
[Authorize]
[RequiresCapability("CAP-HR-TIMETRACK")]
public class TimeTrackingController(IMediator mediator) : ControllerBase
{
    [HttpGet("entries")]
    public async Task<ActionResult<List<TimeEntryResponseModel>>> GetTimeEntries(
        [FromQuery] int? userId,
        [FromQuery] int? jobId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await mediator.Send(new GetTimeEntriesQuery(userId, jobId, from, to));
        return Ok(result);
    }

    [HttpPost("entries")]
    public async Task<ActionResult<TimeEntryResponseModel>> CreateTimeEntry([FromBody] CreateTimeEntryRequestModel request)
    {
        var result = await mediator.Send(new CreateTimeEntryCommand(request));
        return Created($"/api/v1/time-tracking/entries/{result.Id}", result);
    }

    [HttpPatch("entries/{id:int}")]
    public async Task<ActionResult<TimeEntryResponseModel>> UpdateTimeEntry(int id, [FromBody] UpdateTimeEntryRequestModel request)
    {
        var result = await mediator.Send(new UpdateTimeEntryCommand(id, request));
        return Ok(result);
    }

    [HttpPost("timer/start")]
    public async Task<ActionResult<TimeEntryResponseModel>> StartTimer([FromBody] StartTimerRequestModel request)
    {
        var result = await mediator.Send(new StartTimerCommand(request));
        return Created($"/api/v1/time-tracking/entries/{result.Id}", result);
    }

    [HttpPost("timer/stop")]
    public async Task<ActionResult<TimeEntryResponseModel>> StopTimer([FromBody] StopTimerRequestModel request)
    {
        var result = await mediator.Send(new StopTimerCommand(request));
        return Ok(result);
    }

    [HttpGet("clock-status")]
    public async Task<ActionResult<UserClockStatusResponseModel>> GetClockStatus(CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new GetUserClockStatusQuery(userId), ct);
        return Ok(result);
    }

    [HttpGet("clock-events")]
    public async Task<ActionResult<List<ClockEventResponseModel>>> GetClockEvents(
        [FromQuery] int? userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await mediator.Send(new GetClockEventsQuery(userId, from, to));
        return Ok(result);
    }

    [HttpPost("clock-events")]
    public async Task<ActionResult<ClockEventResponseModel>> CreateClockEvent([FromBody] CreateClockEventRequestModel request)
    {
        var result = await mediator.Send(new CreateClockEventCommand(request));
        return Created($"/api/v1/time-tracking/clock-events/{result.Id}", result);
    }

    [HttpDelete("entries/{id:int}")]
    public async Task<IActionResult> DeleteTimeEntry(int id)
    {
        await mediator.Send(new DeleteTimeEntryCommand(id));
        return NoContent();
    }

    // ─── Pay Period ───

    [HttpGet("pay-period")]
    public async Task<ActionResult<PayPeriodResponseModel>> GetCurrentPayPeriod()
    {
        var result = await mediator.Send(new GetCurrentPayPeriodQuery());
        return Ok(result);
    }

    [HttpPut("pay-period/settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdatePayPeriodSettings([FromBody] UpdatePayPeriodSettingsCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }

    [HttpPost("lock-period")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<LockPayPeriodResult>> LockPayPeriod([FromBody] LockPayPeriodCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    // ─── Admin Time Corrections ───

    [HttpPatch("entries/{id:int}/correct")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<TimeEntryResponseModel>> CorrectTimeEntry(int id, [FromBody] AdminCorrectTimeEntryRequestModel request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new AdminCorrectTimeEntryCommand(id, userId, request));
        return Ok(result);
    }

    [HttpGet("corrections")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<TimeCorrectionLogResponseModel>>> GetCorrections(
        [FromQuery] int? userId,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to)
    {
        var result = await mediator.Send(new GetTimeCorrectionsQuery(userId, from, to));
        return Ok(result);
    }

    // ─── Overtime ───

    [HttpGet("overtime/{userId:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<OvertimeBreakdownResponseModel>> GetOvertimeBreakdown(
        int userId, [FromQuery] DateOnly weekOf, CancellationToken ct)
        => Ok(await mediator.Send(new GetOvertimeBreakdownQuery(userId, weekOf), ct));

    [HttpGet("overtime-rules")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<OvertimeRuleResponseModel>>> GetOvertimeRules(CancellationToken ct)
        => Ok(await mediator.Send(new GetOvertimeRulesQuery(), ct));

    [HttpPost("overtime-rules")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<OvertimeRuleResponseModel>> CreateOvertimeRule(
        [FromBody] CreateOvertimeRuleRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateOvertimeRuleCommand(request), ct);
        return Created($"/api/v1/time-tracking/overtime-rules/{result.Id}", result);
    }
}
