using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Preview;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/entity-preview")]
[Authorize]
// Cross-cutting UI navigation aid: the entity-link hover preview must resolve
// regardless of which feature flags an install has. It returns only list-row
// visible basics (identity, status, a date, a headline figure) — the underlying
// entities' own controllers still gate the real data — so it is bootstrap-exempt.
[CapabilityBootstrap]
public class EntityPreviewController(IMediator mediator) : ControllerBase
{
    /// <summary>Returns a lightweight, non-sensitive preview for a linked record, or 404 when the type is unsupported or the record is missing.</summary>
    [HttpGet("{type}/{id:int}")]
    public async Task<ActionResult<EntityPreviewModel>> Get(string type, int id)
    {
        var result = await mediator.Send(new GetEntityPreviewQuery(type, id));
        return result is null ? NotFound() : Ok(result);
    }
}
