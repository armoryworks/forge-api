using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Carriers;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/carriers")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-SHIP")]
public class CarriersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CarrierListItemModel>>> GetCarriers([FromQuery] bool activeOnly = true)
    {
        var result = await mediator.Send(new GetCarriersQuery(activeOnly));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CarrierListItemModel>> CreateCarrier(CreateCarrierRequestModel request)
    {
        var result = await mediator.Send(new CreateCarrierCommand(
            request.Name, request.Code, request.Scac, request.IntegrationKind,
            request.DeliveryUpdateMode, request.IntegrationServiceId,
            request.RequiresScanToShip, request.Notes));
        return CreatedAtAction(nameof(GetCarriers), new { }, result);
    }

    [HttpPut("{id:int}/credentials")]
    public async Task<IActionResult> UpdateCredentials(int id, UpdateCarrierCredentialsRequestModel request)
    {
        await mediator.Send(new UpdateCarrierCredentialsCommand(
            id, request.ClientId, request.Secret, request.AccountNumber, request.Environment));
        return NoContent();
    }
}
