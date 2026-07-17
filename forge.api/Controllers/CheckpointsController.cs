using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Checkpoints;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// One opaque state blob per world id. Built for the factory game adapter's
/// checkpoint persistence — see factory/docs/inventory.md B70. forge-api does
/// not interpret the blob.
/// </summary>
[ApiController]
[Route("api/v1/checkpoints")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer,ProductionWorker")]
[RequiresCapability("CAP-EXT-GAME-ADAPTER")]
public class CheckpointsController(IMediator mediator) : ControllerBase
{
    [HttpPut("{worldId}")]
    public async Task<IActionResult> Put(string worldId, [FromBody] PutCheckpointRequestModel request, CancellationToken ct)
    {
        await mediator.Send(new UpsertCheckpointCommand(worldId, request.Blob), ct);
        return NoContent();
    }

    [HttpGet("{worldId}")]
    public async Task<ActionResult<CheckpointResponseModel>> Get(string worldId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetCheckpointQuery(worldId), ct);
        return result is null ? NotFound() : Ok(result);
    }
}
