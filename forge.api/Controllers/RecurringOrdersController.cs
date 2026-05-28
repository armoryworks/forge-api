using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.RecurringOrders;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/recurring-orders")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-RECURRING")]
public class RecurringOrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<RecurringOrderListItemModel>>> GetRecurringOrders(
        [FromQuery] int? customerId,
        [FromQuery] bool? isActive)
    {
        var result = await mediator.Send(new GetRecurringOrdersQuery(customerId, isActive));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RecurringOrderDetailResponseModel>> GetRecurringOrder(int id)
    {
        var result = await mediator.Send(new GetRecurringOrderByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<RecurringOrderListItemModel>> CreateRecurringOrder(CreateRecurringOrderRequestModel request)
    {
        var result = await mediator.Send(new CreateRecurringOrderCommand(
            request.Name, request.CustomerId, request.ShippingAddressId,
            request.IntervalDays, request.NextGenerationDate, request.Notes,
            request.Lines));
        return CreatedAtAction(nameof(GetRecurringOrder), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<RecurringOrderDetailResponseModel>> UpdateRecurringOrder(
        int id, UpdateRecurringOrderRequestModel request)
    {
        var result = await mediator.Send(new UpdateRecurringOrderCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteRecurringOrder(int id)
    {
        await mediator.Send(new DeleteRecurringOrderCommand(id));
        return NoContent();
    }
}
