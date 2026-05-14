using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.Quotes;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/quotes")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-QUOTE")]
public class QuotesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<QuoteListItemModel>>> GetQuotes(
        [FromQuery] int? customerId,
        [FromQuery] QuoteStatus? status)
    {
        var result = await mediator.Send(new GetQuotesQuery(customerId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<QuoteDetailResponseModel>> GetQuote(int id)
    {
        var result = await mediator.Send(new GetQuoteByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<QuoteListItemModel>> CreateQuote(CreateQuoteRequestModel request)
    {
        var result = await mediator.Send(new CreateQuoteCommand(
            request.CustomerId, request.ShippingAddressId, request.ExpirationDate,
            request.Notes, request.TaxRate, request.Lines));
        return CreatedAtAction(nameof(GetQuote), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(Quote))]
    public async Task<IActionResult> UpdateQuote(int id, UpdateQuoteRequestModel request)
    {
        await mediator.Send(new UpdateQuoteCommand(
            id, request.ShippingAddressId, request.ExpirationDate,
            request.Notes, request.TaxRate));
        return NoContent();
    }

    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> SendQuote(int id)
    {
        await mediator.Send(new SendQuoteCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/accept")]
    public async Task<IActionResult> AcceptQuote(int id)
    {
        await mediator.Send(new AcceptQuoteCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> RejectQuote(int id)
    {
        await mediator.Send(new RejectQuoteCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/convert")]
    public async Task<ActionResult<SalesOrderListItemModel>> ConvertToOrder(int id)
    {
        var result = await mediator.Send(new ConvertQuoteToOrderCommand(id));
        return CreatedAtAction(
            actionName: "GetSalesOrder",
            controllerName: "SalesOrders",
            routeValues: new { id = result.Id },
            value: result);
    }

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(Quote))]
    public async Task<IActionResult> DeleteQuote(int id)
    {
        await mediator.Send(new DeleteQuoteCommand(id));
        return NoContent();
    }
}
