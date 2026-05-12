using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Workflows.Runs;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Workflow Pattern Phase 3 — Workflow run lifecycle endpoints. All
/// authenticated callers can manage their own runs; admin gating only
/// applies to definition / validator authoring.
/// </summary>
[ApiController]
[Route("api/v1/workflows")]
[Authorize]
public class WorkflowsController(IMediator mediator) : ControllerBase
{
    /// <summary>Start a new workflow run; creates the entity row in status='Draft'.</summary>
    [HttpPost]
    public async Task<ActionResult<WorkflowRunResponseModel>> Start(
        [FromBody] StartWorkflowRunRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new StartWorkflowRunCommand(body), ct);
        return Created($"/api/v1/workflows/{result.Id}", result);
    }

    [HttpGet("{runId:int}")]
    public async Task<ActionResult<WorkflowRunResponseModel>> Get(int runId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetWorkflowRunQuery(runId), ct);
        return Ok(result);
    }

    /// <summary>Current user's in-flight runs (resume targets).</summary>
    [HttpGet("active")]
    public async Task<ActionResult<IReadOnlyList<WorkflowRunResponseModel>>> ListActive(CancellationToken ct)
    {
        var result = await mediator.Send(new ListActiveWorkflowRunsQuery(), ct);
        return Ok(result);
    }

    /// <summary>Apply step fields and (if gates pass) advance the pointer.</summary>
    [HttpPatch("{runId:int}/step")]
    public async Task<ActionResult<WorkflowRunResponseModel>> PatchStep(
        int runId,
        [FromBody] PatchWorkflowStepRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new PatchWorkflowStepCommand(runId, body), ct);
        return Ok(result);
    }

    /// <summary>Jump to a different (current or earlier-completed) step.</summary>
    [HttpPatch("{runId:int}/jump")]
    public async Task<ActionResult<WorkflowRunResponseModel>> Jump(
        int runId,
        [FromBody] JumpWorkflowRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new JumpWorkflowCommand(runId, body), ct);
        return Ok(result);
    }

    /// <summary>Mark Complete — runs the entity readiness gate then promotes status.</summary>
    [HttpPost("{runId:int}/complete")]
    public async Task<ActionResult<WorkflowRunResponseModel>> Complete(int runId, CancellationToken ct)
    {
        var result = await mediator.Send(new CompleteWorkflowRunCommand(runId), ct);
        return Ok(result);
    }

    /// <summary>Abandon — soft-deletes the entity if still in Draft.</summary>
    [HttpPost("{runId:int}/abandon")]
    public async Task<ActionResult<WorkflowRunResponseModel>> Abandon(
        int runId,
        [FromBody] AbandonWorkflowRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new AbandonWorkflowCommand(runId, body), ct);
        return Ok(result);
    }

    /// <summary>Toggle express ↔ guided.</summary>
    [HttpPatch("{runId:int}/mode")]
    public async Task<ActionResult<WorkflowRunResponseModel>> SetMode(
        int runId,
        [FromBody] SetWorkflowModeRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new SetWorkflowModeCommand(runId, body), ct);
        return Ok(result);
    }
}
