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
[RequiresCapability("CAP-P2P-BACKTOBACK")]
public class BackToBacksController(IMediator mediator) : ControllerBase
{
    [HttpPost("sales-orders/{soId:int}/lines/{lineId:int}/back-to-back")]
    public async Task<ActionResult> CreateBackToBackOrder(int soId, int lineId, [FromBody] CreateBackToBackRequestModel request)
    {
        var poId = await mediator.Send(new CreateBackToBackOrderCommand(soId, lineId, request));
        return Created($"/api/v1/purchase-orders/{poId}", new { purchaseOrderId = poId });
    }

    [HttpPost("purchase-orders/{poId:int}/lines/{lineId:int}/link-receipt")]
    public async Task<IActionResult> LinkBackToBackReceipt(int poId, int lineId, [FromBody] LinkBackToBackReceiptRequestModel request)
    {
        await mediator.Send(new LinkBackToBackReceiptCommand(poId, lineId, request));
        return NoContent();
    }

    [HttpGet("back-to-back/pending")]
    public async Task<ActionResult<IReadOnlyList<BackToBackStatusResponseModel>>> GetPendingBackToBacks()
    {
        var result = await mediator.Send(new GetPendingBackToBacksQuery());
        return Ok(result);
    }
}
