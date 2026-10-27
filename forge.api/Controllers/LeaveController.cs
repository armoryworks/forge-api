using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Leave;
using Forge.Core.Enums;
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

    [HttpPut("policies/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LeavePolicyResponseModel>> UpdatePolicy(
        int id, [FromBody] UpdateLeavePolicyRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateLeavePolicyCommand(id, request), ct));

    // ── Balances ──

    [HttpGet("balances")]
    [Authorize(Roles = "Admin,Manager,OfficeManager,Controller")]
    public async Task<ActionResult<List<LeaveBalanceResponseModel>>> GetAllBalances(
        [FromQuery] int? policyId, CancellationToken ct)
        => Ok(await mediator.Send(new GetAllLeaveBalancesQuery(policyId), ct));

    [HttpGet("balances/{userId:int}")]
    public async Task<ActionResult<List<LeaveBalanceResponseModel>>> GetBalances(int userId, CancellationToken ct)
        => Ok(await mediator.Send(new GetLeaveBalancesQuery(userId), ct));

    [HttpGet("balances/{userId:int}/adjustments")]
    public async Task<ActionResult<List<LeaveBalanceAdjustmentResponseModel>>> GetBalanceAdjustments(int userId, CancellationToken ct)
        => Ok(await mediator.Send(new GetLeaveBalanceAdjustmentsQuery(userId), ct));

    [HttpPost("balances/{userId:int}/adjust")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<LeaveBalanceAdjustmentResponseModel>> AdjustBalance(
        int userId, [FromBody] AdjustLeaveBalanceRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new AdjustLeaveBalanceCommand(userId, request), ct);
        return Created($"/api/v1/leave/balances/{userId}/adjustments", result);
    }

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

    [HttpPost("requests/{id:int}/cancel")]
    public async Task<IActionResult> CancelRequest(int id, CancellationToken ct)
    {
        await mediator.Send(new CancelLeaveRequestCommand(id), ct);
        return NoContent();
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
