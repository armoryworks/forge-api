using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Workflows.Validators;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Workflow Pattern Phase 3 — Entity readiness validators API.
/// Read endpoints are open to authenticated callers (UI loads them per
/// entity-type page); writes are admin-only. Endpoints are NOT capability-
/// gated since the workflow substrate is foundational and gating would
/// introduce a chicken-and-egg with the workflow capability itself.
/// </summary>
[ApiController]
[Route("api/v1/entity-validators")]
[Authorize]
public class EntityValidatorsController(IMediator mediator) : ControllerBase
{
    /// <summary>List entity readiness validators (optionally filtered by entityType).</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EntityValidatorResponseModel>>> List(
        [FromQuery] string? entityType,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ListEntityValidatorsQuery(entityType), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EntityValidatorResponseModel>> Get(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetEntityValidatorQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EntityValidatorResponseModel>> Create(
        [FromBody] UpsertEntityValidatorRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CreateEntityValidatorCommand(body), ct);
        return Created($"/api/v1/entity-validators/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EntityValidatorResponseModel>> Update(
        int id,
        [FromBody] UpsertEntityValidatorRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateEntityValidatorCommand(id, body), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteEntityValidatorCommand(id), ct);
        return NoContent();
    }
}
