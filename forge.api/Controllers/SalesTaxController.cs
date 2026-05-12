using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.SalesTax;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/sales-tax-rates")]
[Authorize]
[RequiresCapability("CAP-MD-TAXCODES")]
public class SalesTaxController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SalesTaxRateResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetSalesTaxRatesQuery());
        return Ok(result);
    }

    /// <summary>
    /// Returns the currently-effective tax rate for a customer based on their
    /// default billing address state. Falls back to the system default rate.
    /// Returns null (204) if no rate is configured.
    /// </summary>
    [HttpGet("for-customer/{customerId:int}")]
    public async Task<ActionResult<SalesTaxRateResponseModel>> GetForCustomer(int customerId)
    {
        var result = await mediator.Send(new GetTaxRateForCustomerQuery(customerId));
        return result is null ? NoContent() : Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SalesTaxRateResponseModel>> Create([FromBody] CreateSalesTaxRateRequestModel request)
    {
        var result = await mediator.Send(new CreateSalesTaxRateCommand(request));
        return Created($"/api/v1/sales-tax-rates/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SalesTaxRateResponseModel>> Update(int id, [FromBody] CreateSalesTaxRateRequestModel request)
    {
        var result = await mediator.Send(new UpdateSalesTaxRateCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteSalesTaxRateCommand(id));
        return NoContent();
    }
}
