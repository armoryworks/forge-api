using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Vendors;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Vendor id + name pairs for pickers and filters. The Controller reads it too: the payables
/// screen's vendor filter needs it. It is split from <see cref="VendorsController"/> because that
/// controller's role list is ANDed with any action-level list and leaves the Controller out.
/// </summary>
[ApiController]
[Route("api/v1/vendors")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Procurement,Controller")]
[RequiresCapability("CAP-MD-VENDORS")]
public class VendorDropdownController(IMediator mediator) : ControllerBase
{
    [HttpGet("dropdown")]
    public async Task<ActionResult<List<VendorResponseModel>>> GetVendorDropdown()
    {
        var result = await mediator.Send(new GetVendorDropdownQuery());
        return Ok(result);
    }
}
