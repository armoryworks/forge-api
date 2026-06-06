using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — vendor payments (AP cash disbursement). The AP twin of
/// <see cref="PaymentsController"/>. Creating a payment is the cash-disbursement posting trigger; while
/// CAP-ACCT-FULLGL is OFF the posting self-no-ops. Gated by <c>CAP-P2P-RECEIVE</c>.
/// </summary>
[ApiController]
[Route("api/v1/vendor-payments")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
// Baseline P2P capability (default-on). A dedicated CAP-P2P-PAY (symmetric to AR's CAP-O2C-CASH)
// is the recommended end-state — see PHASE2_STATUS "capability taxonomy".
[RequiresCapability("CAP-P2P-PO")]
public class VendorPaymentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorPaymentListItemModel>>> GetVendorPayments(
        [FromQuery] int? vendorId,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorPaymentsQuery(vendorId), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorPaymentListItemModel>> GetVendorPayment(int id)
        => Ok(await mediator.Send(new GetVendorPaymentByIdQuery(id)));

    [HttpPost]
    public async Task<ActionResult<VendorPaymentListItemModel>> CreateVendorPayment(CreateVendorPaymentRequestModel request)
    {
        var result = await mediator.Send(new CreateVendorPaymentCommand(
            request.VendorId, request.Method, request.Amount, request.PaymentDate,
            request.ReferenceNumber, request.Notes, request.Applications));
        return CreatedAtAction(nameof(GetVendorPayment), new { id = result.Id }, result);
    }
}
