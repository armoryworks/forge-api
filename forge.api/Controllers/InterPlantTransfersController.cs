using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/inventory/transfers")]
[Authorize]
[RequiresCapability("CAP-INV-MULTILOC")]
public class InterPlantTransfersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<InterPlantTransferResponseModel>>> GetTransfers(
        [FromQuery] InterPlantTransferStatus? status = null,
        [FromQuery] int? plantId = null)
    {
        var result = await mediator.Send(new GetInterPlantTransfersQuery(status, plantId));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<InterPlantTransferResponseModel>> CreateTransfer([FromBody] CreateInterPlantTransferRequestModel request)
    {
        var result = await mediator.Send(new CreateInterPlantTransferCommand(request));
        return CreatedAtAction(nameof(GetTransfers), new { }, result);
    }

    [HttpPost("{id:int}/ship")]
    public async Task<IActionResult> ShipTransfer(int id, [FromBody] ShipTransferRequestModel request)
    {
        await mediator.Send(new ShipInterPlantTransferCommand(id, request));
        return NoContent();
    }

    [HttpPost("{id:int}/receive")]
    public async Task<IActionResult> ReceiveTransfer(int id, [FromBody] List<ReceiveTransferLineRequestModel> lines)
    {
        await mediator.Send(new ReceiveInterPlantTransferCommand(id, lines));
        return NoContent();
    }
}
