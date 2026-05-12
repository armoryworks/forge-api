using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.EntityCapabilityRequirements;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Admin CRUD for entity-capability requirement rows. Authors use the
/// <c>/admin/entity-completeness</c> page (Phase 4 capability admin
/// surface) to add / edit / delete rows. Reads are admin-only too —
/// no operational pages depend on the raw rows; consumers go through
/// <c>EntityCompletenessController</c> which evaluates the predicates.
///
/// Catalog ships empty per Dan's option-B choice.
/// </summary>
[ApiController]
[Route("api/v1/admin/entity-capability-requirements")]
[Authorize(Roles = "Admin")]
public class EntityCapabilityRequirementsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<EntityCapabilityRequirementResponseModel>>> List(
        [FromQuery] string? entityType,
        [FromQuery] string? capabilityCode,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new ListEntityCapabilityRequirementsQuery(entityType, capabilityCode), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EntityCapabilityRequirementResponseModel>> Get(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetEntityCapabilityRequirementQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<EntityCapabilityRequirementResponseModel>> Create(
        [FromBody] UpsertEntityCapabilityRequirementRequestModel body,
        CancellationToken ct)
    {
        var created = await mediator.Send(new CreateEntityCapabilityRequirementCommand(body), ct);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<EntityCapabilityRequirementResponseModel>> Update(
        int id,
        [FromBody] UpsertEntityCapabilityRequirementRequestModel body,
        CancellationToken ct)
    {
        var updated = await mediator.Send(new UpdateEntityCapabilityRequirementCommand(id, body), ct);
        return Ok(updated);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteEntityCapabilityRequirementCommand(id), ct);
        return NoContent();
    }
}
