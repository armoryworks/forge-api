using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Shipments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/addresses")]
[Authorize]
public class AddressesController(IMediator mediator) : ControllerBase
{
    [HttpPost("validate")]
    public async Task<ActionResult<AddressValidationResponseModel>> Validate(ValidateAddressRequestModel request)
    {
        var result = await mediator.Send(new ValidateShippingAddressCommand(request));
        return Ok(result);
    }
}
