using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Andon;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/display/shop-floor/andon")]
[Authorize]
[RequiresCapability("CAP-MFG-SHOPFLOOR")]
public class ShopFloorAndonController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [RequiresCapability("CAP-EXT-ANDON")]
    public async Task<ActionResult<RaiseKioskAndonResponseModel>> RaiseAndon(
        [FromBody] RaiseKioskAndonRequestModel model, CancellationToken ct)
    {
        if (!int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var callerId))
            return Unauthorized();

        var result = await mediator.Send(new RaiseKioskAndonCommand(model.JobId, model.Type, model.Notes, callerId), ct);
        return Created($"/api/v1/shop-floor/andon/alerts?workCenterId={result.WorkCenterId}", result);
    }
}
