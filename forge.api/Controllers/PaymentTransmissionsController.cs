using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.PaymentTransmissions;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — electronic bank/ACH transmission triage: list transmissions (filterable by
/// status for the failed-payment queue) and manually re-queue Failed/Cancelled ones. Reuses the
/// baseline AP capability (<c>CAP-P2P-PO</c>) like <see cref="VendorPaymentsController"/>.
/// </summary>
[ApiController]
[Route("api/v1/payment-transmissions")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-P2P-PO")]
public class PaymentTransmissionsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PaymentTransmissionListItemModel>>> GetPaymentTransmissions(
        [FromQuery] PaymentTransmissionStatus? status,
        [FromQuery] string? sourceType,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetPaymentTransmissionsQuery(status, sourceType), ct));

    [HttpPost("{id:int}/retry")]
    public async Task<ActionResult<PaymentTransmissionListItemModel>> RetryPaymentTransmission(
        int id, CancellationToken ct)
        => Ok(await mediator.Send(new RetryPaymentTransmissionCommand(id), ct));
}
