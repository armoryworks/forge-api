using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Leave;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/leave")]
[Authorize]
[RequiresCapability("CAP-HR-LEAVE")]
public class LeaveController(IMediator mediator) : ControllerBase
{
    // ── Policies (Admin) ──

    [HttpGet("policies")]
    public async Task<ActionResult<List<LeavePolicyResponseModel>>> GetPolicies(
        [FromQuery] bool activeOnly = true, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetLeavePoliciesQuery(activeOnly), ct));

    [HttpPost("policies")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LeavePolicyResponseModel>> CreatePolicy(
        [FromBody] CreateLeavePolicyRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateLeavePolicyCommand(request), ct);
        return Created($"/api/v1/leave/policies/{result.Id}", result);
    }

    // ── Balances ──

    [HttpGet("balances/{userId:int}")]
    public async Task<ActionResult<List<LeaveBalanceResponseModel>>> GetBalances(int userId, CancellationToken ct)
        => Ok(await mediator.Send(new GetLeaveBalancesQuery(userId), ct));

    // ── Requests ──

    [HttpGet("requests")]
    public async Task<ActionResult<List<LeaveRequestResponseModel>>> GetRequests(
        [FromQuery] int? userId, [FromQuery] LeaveRequestStatus? status, CancellationToken ct)
        => Ok(await mediator.Send(new GetLeaveRequestsQuery(userId, status), ct));

    [HttpPost("requests")]
    public async Task<ActionResult<LeaveRequestResponseModel>> CreateRequest(
        [FromBody] CreateLeaveRequestModel request, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new CreateLeaveRequestCommand(request, userId), ct);
        return Created($"/api/v1/leave/requests/{result.Id}", result);
    }

    [HttpPost("requests/{id:int}/approve")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ApproveRequest(int id, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new DecideLeaveRequestCommand(id, true, userId), ct);
        return NoContent();
    }

    [HttpPost("requests/{id:int}/deny")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DenyRequest(int id, [FromBody] DenyLeaveRequestBody? body, CancellationToken ct)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new DecideLeaveRequestCommand(id, false, userId, body?.Reason), ct);
        return NoContent();
    }
}

public record DenyLeaveRequestBody(string? Reason);
