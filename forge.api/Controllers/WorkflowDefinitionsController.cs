using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Workflows.Definitions;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Workflow Pattern Phase 3 — Workflow definitions API. Reads open to all
/// authenticated callers; writes admin-only.
/// </summary>
[ApiController]
[Route("api/v1/workflow-definitions")]
[Authorize]
public class WorkflowDefinitionsController(IMediator mediator) : ControllerBase
{
    /// <summary>List workflow definitions (optionally filtered by entityType).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<WorkflowDefinitionResponseModel>>> List(
        [FromQuery] string? entityType,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ListWorkflowDefinitionsQuery(entityType), ct);
        return Ok(result);
    }

    [HttpGet("{definitionId}")]
    public async Task<ActionResult<WorkflowDefinitionResponseModel>> Get(string definitionId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetWorkflowDefinitionQuery(definitionId), ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<WorkflowDefinitionResponseModel>> Create(
        [FromBody] UpsertWorkflowDefinitionRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CreateWorkflowDefinitionCommand(body), ct);
        return Created($"/api/v1/workflow-definitions/{result.DefinitionId}", result);
    }

    [HttpPut("{definitionId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<WorkflowDefinitionResponseModel>> Update(
        string definitionId,
        [FromBody] UpsertWorkflowDefinitionRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateWorkflowDefinitionCommand(definitionId, body), ct);
        return Ok(result);
    }

    [HttpDelete("{definitionId}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(string definitionId, CancellationToken ct)
    {
        await mediator.Send(new DeleteWorkflowDefinitionCommand(definitionId), ct);
        return NoContent();
    }
}
