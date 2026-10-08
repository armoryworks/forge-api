using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Parts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Starts an empty routing from another part's operations. Part master data, so it carries the
/// parts-master capability and the engineering write roles.
/// </summary>
[ApiController]
[Route("api/v1/parts/{id:int}/routing")]
[Authorize(Roles = "Admin,Manager,Engineer")]
[RequiresCapability("CAP-MD-PARTS")]
public class RoutingCopyController(IMediator mediator) : ControllerBase
{
    [HttpPost("copy-from/{sourcePartId:int}")]
    public async Task<ActionResult<List<OperationResponseModel>>> CopyFrom(int id, int sourcePartId, CancellationToken ct)
        => StatusCode(201, await mediator.Send(new CopyRoutingCommand(id, sourcePartId), ct));
}
