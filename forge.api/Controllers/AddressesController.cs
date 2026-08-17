using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/addresses")]
[Authorize]
// address validation is a cross-entity input helper (customer, vendor, company location, ship-to); no single capability owns it and gating it breaks address forms everywhere
[CapabilityBootstrap]
public class AddressesController(IMediator mediator) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<ActionResult<AddressValidationResponseModel>> Validate(ValidateAddressRequestModel request)
    {
        var result = await mediator.Send(new ValidateShippingAddressCommand(request));
        return Ok(result);
    }
}
