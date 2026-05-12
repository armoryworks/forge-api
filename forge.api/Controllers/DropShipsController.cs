using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer")]
[RequiresCapability("CAP-P2P-DROPSHIP")]
public class DropShipsController(IMediator mediator) : ControllerBase
{
    [HttpPost("sales-orders/{soId:int}/lines/{lineId:int}/drop-ship")]
    public async Task<ActionResult> CreateDropShipOrder(int soId, int lineId, [FromBody] CreateDropShipRequestModel request)
    {
        var poId = await mediator.Send(new CreateDropShipOrderCommand(soId, lineId, request));
        return Created($"/api/v1/purchase-orders/{poId}", new { purchaseOrderId = poId });
    }

    [HttpPost("purchase-orders/{poId:int}/lines/{lineId:int}/drop-ship-confirm")]
    public async Task<IActionResult> ConfirmDropShipDelivery(int poId, int lineId, [FromBody] ConfirmDropShipDeliveryRequestModel request)
    {
        await mediator.Send(new ConfirmDropShipDeliveryCommand(poId, lineId, request));
        return NoContent();
    }

    [HttpGet("drop-ships/pending")]
    public async Task<ActionResult<IReadOnlyList<DropShipStatusResponseModel>>> GetPendingDropShips()
    {
        var result = await mediator.Send(new GetPendingDropShipsQuery());
        return Ok(result);
    }
}
