using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.CustomerReturns;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/customer-returns")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-RMA")]
public class CustomerReturnsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CustomerReturnListItemModel>>> GetReturns(
        [FromQuery] int? customerId,
        [FromQuery] CustomerReturnStatus? status)
    {
        var result = await mediator.Send(new GetCustomerReturnsQuery(customerId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerReturnDetailResponseModel>> GetReturn(int id)
    {
        var result = await mediator.Send(new GetCustomerReturnByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerReturnListItemModel>> CreateReturn(CreateCustomerReturnRequestModel request)
    {
        var result = await mediator.Send(new CreateCustomerReturnCommand(
            request.CustomerId, request.OriginalJobId, request.Reason,
            request.Notes, request.ReturnDate, request.CreateReworkJob));
        return CreatedAtAction(nameof(GetReturn), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateReturn(int id, UpdateCustomerReturnRequestModel request)
    {
        await mediator.Send(new UpdateCustomerReturnCommand(id, request.Reason, request.Notes, request.InspectionNotes));
        return NoContent();
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> ResolveReturn(int id)
    {
        await mediator.Send(new ResolveCustomerReturnCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> CloseReturn(int id)
    {
        await mediator.Send(new CloseCustomerReturnCommand(id));
        return NoContent();
    }
}
