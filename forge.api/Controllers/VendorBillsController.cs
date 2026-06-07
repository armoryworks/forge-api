using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorBills;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — vendor bills (AP). The AP twin of <see cref="InvoicesController"/>. Approval
/// is the AP posting trigger; while CAP-ACCT-FULLGL is OFF the posting self-no-ops. Gated by
/// <c>CAP-P2P-RECEIVE</c> (the AP-posting P2P capability — "vendor invoice before AP posts").
/// </summary>
[ApiController]
[Route("api/v1/vendor-bills")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
// Baseline P2P capability (default-on). A dedicated CAP-P2P-BILL (symmetric to AR's CAP-O2C-INVOICE)
// is the recommended end-state — see PHASE2_STATUS "capability taxonomy".
[RequiresCapability("CAP-P2P-PO")]
public class VendorBillsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorBillListItemModel>>> GetVendorBills(
        [FromQuery] int? vendorId,
        [FromQuery] VendorBillStatus? status,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorBillsQuery(vendorId, status), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorBillListItemModel>> GetVendorBill(int id)
        => Ok(await mediator.Send(new GetVendorBillByIdQuery(id)));

    [HttpPost]
    public async Task<ActionResult<VendorBillListItemModel>> CreateVendorBill(CreateVendorBillRequestModel request)
    {
        var result = await mediator.Send(new CreateVendorBillCommand(
            request.VendorId, request.VendorInvoiceNumber, request.PurchaseOrderId,
            request.BillDate, request.DueDate, request.TaxAmount, request.Notes, request.Lines));
        return CreatedAtAction(nameof(GetVendorBill), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/approve")]
    public async Task<IActionResult> ApproveVendorBill(int id)
    {
        await mediator.Send(new ApproveVendorBillCommand(id));
        return NoContent();
    }

    /// <summary>
    /// Voids a vendor bill. A Draft bill is cancelled; an Approved bill is reversed (the AP/expense journal
    /// is reversed and the billed quantity returned to its PO lines). Blocked if payments are applied.
    /// </summary>
    [HttpPost("{id:int}/void")]
    public async Task<IActionResult> VoidVendorBill(int id)
    {
        await mediator.Send(new VoidVendorBillCommand(id));
        return NoContent();
    }
}
