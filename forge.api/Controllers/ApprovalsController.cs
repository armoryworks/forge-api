using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Approvals;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/approvals")]
[Authorize]
[RequiresCapability("CAP-P2P-APPROVALS")]
public class ApprovalsController(IMediator mediator) : ControllerBase
{
    [HttpGet("pending")]
    public async Task<ActionResult<List<ApprovalRequestResponseModel>>> GetPendingApprovals()
    {
        var userId = GetUserId();
        var result = await mediator.Send(new GetPendingApprovalsQuery(userId));
        return Ok(result);
    }

    [HttpGet("history/{entityType}/{entityId:int}")]
    public async Task<ActionResult<List<ApprovalRequestResponseModel>>> GetApprovalHistory(
        string entityType, int entityId)
    {
        var result = await mediator.Send(new GetApprovalHistoryQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpPost("submit")]
    public async Task<ActionResult<ApprovalRequestResponseModel>> SubmitForApproval(
        [FromBody] SubmitApprovalRequestModel data)
    {
        var userId = GetUserId();
        var result = await mediator.Send(new SubmitForApprovalCommand(data, userId));
        if (result == null) return Ok(new { approvalRequired = false });
        return Created($"/api/v1/approvals/{result.Id}", result);
    }

    [HttpPost("{requestId:int}/approve")]
    public async Task<ActionResult<ApprovalRequestResponseModel>> Approve(
        int requestId, [FromBody] ApprovalActionRequestModel? data)
    {
        var userId = GetUserId();
        var result = await mediator.Send(new ApproveRequestCommand(requestId, userId, data?.Comments));
        return Ok(result);
    }

    [HttpPost("{requestId:int}/reject")]
    public async Task<ActionResult<ApprovalRequestResponseModel>> Reject(
        int requestId, [FromBody] ApprovalActionRequestModel data)
    {
        var userId = GetUserId();
        var result = await mediator.Send(new RejectRequestCommand(requestId, userId, data.Comments ?? ""));
        return Ok(result);
    }

    [HttpPost("{requestId:int}/delegate")]
    public async Task<IActionResult> Delegate(
        int requestId, [FromBody] DelegateApprovalRequestModel data)
    {
        var userId = GetUserId();
        await mediator.Send(new DelegateRequestCommand(requestId, userId, data));
        return NoContent();
    }

    // ── Admin ──

    [HttpGet("workflows")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<ApprovalWorkflowResponseModel>>> GetWorkflows()
    {
        var result = await mediator.Send(new GetApprovalWorkflowsQuery());
        return Ok(result);
    }

    [HttpPost("workflows")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApprovalWorkflowResponseModel>> CreateWorkflow(
        [FromBody] CreateApprovalWorkflowRequestModel data)
    {
        var result = await mediator.Send(new CreateApprovalWorkflowCommand(data));
        return Created($"/api/v1/approvals/workflows/{result.Id}", result);
    }

    [HttpPut("workflows/{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApprovalWorkflowResponseModel>> UpdateWorkflow(
        int id, [FromBody] CreateApprovalWorkflowRequestModel data)
    {
        var result = await mediator.Send(new UpdateApprovalWorkflowCommand(id, data));
        return Ok(result);
    }

    private int GetUserId()
    {
        var claim = User.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(claim, out var id) ? id : throw new UnauthorizedAccessException();
    }
}
