using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Pricing;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/pricing")]
[Authorize]
[RequiresCapability("CAP-MD-PRICELIST")]
public class PricingController(IMediator mediator) : ControllerBase
{
    [HttpGet("resolve")]
    public async Task<ActionResult<PriceResolutionResponseModel>> ResolvePrice(
        [FromQuery] int partId, [FromQuery] int? customerId, [FromQuery] int quantity = 1)
    {
        var result = await mediator.Send(new ResolvePriceQuery(partId, customerId, quantity));
        return Ok(result);
    }
}
