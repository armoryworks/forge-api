using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Vendors.Contacts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/vendors/{vendorId:int}/contacts")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-VENDORS")]
public class VendorContactsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorContactResponseModel>>> GetContacts(
        int vendorId, [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var result = await mediator.Send(new GetVendorContactsQuery(vendorId, includeInactive), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<VendorContactResponseModel>> CreateContact(
        int vendorId, CreateVendorContactRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateVendorContactCommand(
            vendorId, request.FirstName, request.LastName, request.Email, request.Phone,
            request.Mobile, request.Fax, request.Role, request.IsPrimary, request.Notes,
            request.IsActive), ct);
        return CreatedAtAction(nameof(GetContacts), new { vendorId }, result);
    }

    [HttpPut("{contactId:int}")]
    public async Task<ActionResult<VendorContactResponseModel>> UpdateContact(
        int vendorId, int contactId, UpdateVendorContactRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateVendorContactCommand(
            vendorId, contactId, request.FirstName, request.LastName, request.Email,
            request.Phone, request.Mobile, request.Fax, request.Role, request.IsPrimary,
            request.IsActive, request.Notes), ct);
        return Ok(result);
    }

    [HttpDelete("{contactId:int}")]
    public async Task<IActionResult> DeleteContact(int vendorId, int contactId, CancellationToken ct)
    {
        await mediator.Send(new DeleteVendorContactCommand(vendorId, contactId), ct);
        return NoContent();
    }
}
