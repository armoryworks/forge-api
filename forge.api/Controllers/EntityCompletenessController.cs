using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.EntityCompleteness;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Per-entity completeness lookup driving the
/// <c>&lt;app-entity-completeness-chip&gt;</c> +
/// <c>&lt;app-entity-completeness-badge&gt;</c> in the UI. Returns a list
/// of every currently-enabled capability that has requirements declared
/// for the entity type, with a per-capability ok/missing breakdown.
///
/// Read-only and not capability-gated — the chip is foundational UX that
/// surfaces status of OTHER capabilities; gating the chip endpoint itself
/// would create a chicken-and-egg.
/// </summary>
[ApiController]
[Route("api/v1/entities/{entityType}/{entityId:int}/completeness")]
[Authorize]
public class EntityCompletenessController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<EntityCompletenessResponseModel>> Get(
        string entityType,
        int entityId,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetEntityCompletenessQuery(entityType, entityId), ct);
        return Ok(result);
    }
}
