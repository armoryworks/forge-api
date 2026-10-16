using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.PaymentTransmissions;
using Forge.Api.Features.VendorPayments;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Vendor payment list and detail, and the payment transmission queue, for payables review. The
/// Controller reads them; recording, voiding and retrying stay with
/// <see cref="VendorPaymentsController"/> and <see cref="PaymentTransmissionsController"/>, whose
/// role lists are ANDed with any action-level list.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager,OfficeManager,Controller")]
[RequiresCapability("CAP-P2P-PAY")]
public class VendorPaymentReadsController(IMediator mediator) : ControllerBase
{
    [HttpGet("api/v1/vendor-payments")]
    public async Task<ActionResult<List<VendorPaymentListItemModel>>> GetVendorPayments(
        [FromQuery] int? vendorId,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorPaymentsQuery(vendorId), ct));

    [HttpGet("api/v1/vendor-payments/{id:int}")]
    public async Task<ActionResult<VendorPaymentDetailModel>> GetVendorPayment(int id)
        => Ok(await mediator.Send(new GetVendorPaymentByIdQuery(id)));

    [HttpGet("api/v1/payment-transmissions")]
    public async Task<ActionResult<List<PaymentTransmissionListItemModel>>> GetPaymentTransmissions(
        [FromQuery] PaymentTransmissionStatus? status,
        [FromQuery] string? sourceType,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetPaymentTransmissionsQuery(status, sourceType), ct));
}
