using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorBills;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Vendor bill list and detail. Procurement reads bills against its purchase orders and the
/// Controller reviews payables; neither creates, approves or voids a bill. Kept out of
/// <see cref="VendorBillsController"/> because that controller's role list is ANDed with any
/// action-level list.
/// </summary>
[ApiController]
[Route("api/v1/vendor-bills")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Procurement,Controller")]
[RequiresCapability("CAP-P2P-BILL")]
public class VendorBillReadsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorBillListItemModel>>> GetVendorBills(
        [FromQuery] int? vendorId,
        [FromQuery] VendorBillStatus? status,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorBillsQuery(vendorId, status), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorBillDetailModel>> GetVendorBill(int id)
        => Ok(await mediator.Send(new GetVendorBillByIdQuery(id)));
}
