using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.PaymentTerms;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// The payment terms master (Net 30, 2/10 Net 30, ...). Any signed-in user can read the list for the terms
/// pickers on customers, vendors, orders, invoices and bills; the finance roles maintain it.
/// </summary>
[ApiController]
[Route("api/v1/payment-terms")]
[Authorize]
[RequiresAnyCapability("CAP-MD-CUSTOMERS", "CAP-MD-VENDORS")]
public class PaymentTermsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PaymentTermResponseModel>>> GetPaymentTerms([FromQuery] bool includeInactive = false)
        => Ok(await mediator.Send(new GetPaymentTermsQuery(includeInactive)));

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<PaymentTermResponseModel>> CreatePaymentTerm([FromBody] PaymentTermRequestModel request)
    {
        var result = await mediator.Send(new CreatePaymentTermCommand(request));
        return Created($"/api/v1/payment-terms/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<PaymentTermResponseModel>> UpdatePaymentTerm(int id, [FromBody] PaymentTermRequestModel request)
        => Ok(await mediator.Send(new UpdatePaymentTermCommand(id, request)));
}
