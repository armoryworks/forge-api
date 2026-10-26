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
/// CAP-ACCT-FULLGL is OFF the posting self-no-ops. Gated by <c>CAP-P2P-PAY</c>.
/// </summary>
[ApiController]
[Route("api/v1/vendor-payments")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
// Dedicated AP capability (default-on, split from CAP-P2P-PO, symmetric to AR's CAP-O2C-CASH —
// see docs/accounting/PHASE2_STATUS.md "capability taxonomy").
[RequiresCapability("CAP-P2P-PAY")]
public class VendorPaymentsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<VendorPaymentListItemModel>> CreateVendorPayment(CreateVendorPaymentRequestModel request)
    {
        var result = await mediator.Send(new CreateVendorPaymentCommand(
            request.VendorId, request.Method, request.Amount, request.PaymentDate,
            request.ReferenceNumber, request.Notes, request.Applications,
            request.CurrencyId, request.FxRate));
        return CreatedAtAction(nameof(VendorPaymentReadsController.GetVendorPayment), "VendorPaymentReads", new { id = result.Id }, result);
    }

    /// <summary>
    /// Pays the bills picked from the payment run: one vendor payment per vendor and currency, each through the
    /// normal vendor-payment create (segregation of duties, posting and ACH batching unchanged).
    /// </summary>
    [HttpPost("from-suggestions")]
    public async Task<ActionResult<List<VendorPaymentListItemModel>>> CreateFromSuggestions(
        CreateVendorPaymentsFromSuggestionsRequestModel request)
        => Ok(await mediator.Send(new CreateVendorPaymentsFromSuggestionsCommand(request)));

    /// <summary>
    /// Voids a vendor payment: drops its bill applications (bills reopen), reverses the cash-disbursement
    /// journal (FULLGL-respecting), cancels any pending bank transmission, and soft-deletes the payment.
    /// Rejected (409) once the latest transmission has Succeeded — money already moved.
    /// </summary>
    [HttpPost("{id:int}/void")]
    public async Task<IActionResult> VoidVendorPayment(int id, VoidPaymentRequestModel request)
    {
        await mediator.Send(new VoidVendorPaymentCommand(id, request.Reason));
        return NoContent();
    }

    /// <summary>
    /// banking.wire.manual-attestation: a SECOND user attests the wire was entered at the bank
    /// portal — flips the Queued transmission to Succeeded and clears cash-in-transit (the wire
    /// twin of payment-batch release; SoD enforced server-side).
    /// </summary>
    [HttpPost("{id:int}/attest-wire")]
    public async Task<IActionResult> AttestWire(int id, AttestWireRequestModel request)
    {
        await mediator.Send(new AttestWireTransmissionCommand(id, request.BankReference));
        return NoContent();
    }
}
