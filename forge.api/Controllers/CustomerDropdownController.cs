using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Customers;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Customer id + name pairs for pickers and filters. Production workers read it too: the
/// kanban board's customer filter and the new-job dialog both need it, and a job already
/// shows its customer's name. It is split from <see cref="CustomersController"/> because that
/// controller's role list is ANDed with any action-level list and leaves production workers out.
/// </summary>
[ApiController]
[Route("api/v1/customers")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM,Engineer,ProductionWorker")]
[RequiresCapability("CAP-MD-CUSTOMERS")]
public class CustomerDropdownController(IMediator mediator) : ControllerBase
{
    [HttpGet("dropdown")]
    public async Task<ActionResult<List<CustomerResponseModel>>> GetCustomerDropdown()
    {
        var result = await mediator.Send(new GetCustomersQuery());
        return Ok(result);
    }
}
