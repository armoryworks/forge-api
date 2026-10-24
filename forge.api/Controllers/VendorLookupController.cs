using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Vendors;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Active-vendor search for pickers, returning id, name and code only. The Engineer reads it to
/// name a subcontract vendor on a routing step and to add a vendor source to a part without
/// being granted the vendor master that <see cref="VendorsController"/> serves.
/// </summary>
[ApiController]
[Route("api/v1/vendors")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Procurement,Engineer")]
[RequiresCapability("CAP-MD-VENDORS")]
public class VendorLookupController(IMediator mediator) : ControllerBase
{
    [HttpGet("lookup")]
    public async Task<ActionResult<List<VendorLookupModel>>> Lookup(
        [FromQuery] string? q,
        [FromQuery] string? search,
        [FromQuery] int pageSize = 10,
        CancellationToken ct = default)
    {
        var term = string.IsNullOrWhiteSpace(q) ? search : q;
        var result = await mediator.Send(new GetVendorLookupQuery(term, pageSize), ct);
        return Ok(result);
    }
}
