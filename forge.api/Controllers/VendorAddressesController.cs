using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Vendors.Addresses;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/vendors/{vendorId:int}/addresses")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-VENDORS")]
public class VendorAddressesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorAddressResponseModel>>> GetAddresses(
        int vendorId, [FromQuery] bool includeInactive, CancellationToken ct)
    {
        var result = await mediator.Send(new GetVendorAddressesQuery(vendorId, includeInactive), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<VendorAddressResponseModel>> CreateAddress(
        int vendorId, CreateVendorAddressRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateVendorAddressCommand(
            vendorId, request.Label, request.AddressType, request.Line1, request.Line2,
            request.City, request.State, request.PostalCode, request.Country,
            request.IsDefault, request.IsActive), ct);
        return CreatedAtAction(nameof(GetAddresses), new { vendorId }, result);
    }

    [HttpPut("{addressId:int}")]
    public async Task<ActionResult<VendorAddressResponseModel>> UpdateAddress(
        int vendorId, int addressId, UpdateVendorAddressRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateVendorAddressCommand(
            vendorId, addressId, request.Label, request.AddressType, request.Line1, request.Line2,
            request.City, request.State, request.PostalCode, request.Country,
            request.IsDefault, request.IsActive), ct);
        return Ok(result);
    }

    [HttpDelete("{addressId:int}")]
    public async Task<IActionResult> DeleteAddress(int vendorId, int addressId, CancellationToken ct)
    {
        await mediator.Send(new DeleteVendorAddressCommand(vendorId, addressId), ct);
        return NoContent();
    }
}
