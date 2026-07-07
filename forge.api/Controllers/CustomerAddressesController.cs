using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.CustomerAddresses;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/customers/{customerId:int}/addresses")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
// Wave 5 — multi-address management (Addresses tab + CRUD) gated behind
// CAP-MD-CUSTOMER-ADDRESSES. Default ON; admins toggle off when every
// customer has one address used for both billing and shipping (the
// single-address shape lives on the customer record itself, written by
// CreateCustomer → customer.Addresses.Add at create time).
//
// CAP-MD-CUSTOMER-ADDRESSES depends on CAP-MD-CUSTOMERS (dependency edge
// in CapabilityCatalogRelations), so the customer master is implicitly
// required — only one decorator at the controller level.
[RequiresCapability("CAP-MD-CUSTOMER-ADDRESSES")]
public class CustomerAddressesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CustomerAddressResponseModel>>> GetAddresses(
        int customerId, [FromQuery] bool includeInactive = false)
    {
        // Inactive addresses are admin-only context; other roles silently get
        // the active set even if they pass the flag.
        var showInactive = includeInactive && User.IsInRole("Admin");
        var result = await mediator.Send(new GetCustomerAddressesQuery(customerId, showInactive));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerAddressResponseModel>> CreateAddress(
        int customerId, CreateCustomerAddressRequestModel request)
    {
        var result = await mediator.Send(new CreateCustomerAddressCommand(
            customerId, request.Label, request.AddressType, request.Line1,
            request.Line2, request.City, request.State, request.PostalCode,
            request.Country, request.IsDefault));
        return CreatedAtAction(nameof(GetAddresses), new { customerId }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateAddress(int customerId, int id, UpdateCustomerAddressRequestModel request)
    {
        await mediator.Send(new UpdateCustomerAddressCommand(
            id, request.Label, request.AddressType, request.Line1,
            request.Line2, request.City, request.State, request.PostalCode,
            request.Country, request.IsDefault));
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAddress(int customerId, int id)
    {
        await mediator.Send(new DeleteCustomerAddressCommand(id));
        return NoContent();
    }

    /// <summary>Admin-only active/inactive toggle — see SetCustomerAddressActive.</summary>
    [HttpPatch("{id:int}/active")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> SetAddressActive(int customerId, int id, SetAddressActiveRequestModel request)
    {
        await mediator.Send(new SetCustomerAddressActiveCommand(id, request.IsActive));
        return NoContent();
    }
}
