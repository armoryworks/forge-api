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
/// <c>CAP-P2P-BILL</c> (the dedicated AP bill capability, symmetric to AR's CAP-O2C-INVOICE).
/// </summary>
[ApiController]
[Route("api/v1/vendor-bills")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
// Dedicated AP capability (default-on, split from CAP-P2P-PO per the owner-ratified AP capability
// taxonomy — see docs/accounting/PHASE2_STATUS.md "capability taxonomy").
[RequiresCapability("CAP-P2P-BILL")]
public class VendorBillsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<VendorBillListItemModel>> CreateVendorBill(CreateVendorBillRequestModel request)
    {
        var result = await mediator.Send(new CreateVendorBillCommand(
            request.VendorId, request.VendorInvoiceNumber, request.PurchaseOrderId,
            request.BillDate, request.DueDate, request.TaxAmount, request.Notes, request.Lines,
            CurrencyId: request.CurrencyId, FxRate: request.FxRate, PaymentTermsId: request.PaymentTermsId));
        return CreatedAtAction(nameof(VendorBillReadsController.GetVendorBill), "VendorBillReads", new { id = result.Id }, result);
    }

    /// <summary>
    /// The GL accounts a standalone bill line can debit directly (active, postable, non-control accounts of
    /// the active book), for the bill dialog's account picker.
    /// </summary>
    [HttpGet("billable-accounts")]
    public async Task<ActionResult<List<BillableGlAccountModel>>> GetBillableAccounts(CancellationToken ct)
        => Ok(await mediator.Send(new GetBillableGlAccountsQuery(), ct));

    /// <summary>
    /// The payment run: approved bills due by <paramref name="dueThrough"/> (default the pay date) or with an
    /// early-payment discount still open on <paramref name="payDate"/>, each with the suggested cash and discount.
    /// </summary>
    [HttpGet("payment-suggestions")]
    [RequiresCapability("CAP-P2P-PAY")]
    public async Task<ActionResult<List<VendorPaymentSuggestionModel>>> GetPaymentSuggestions(
        [FromQuery] DateTimeOffset payDate, [FromQuery] DateTimeOffset? dueThrough, [FromQuery] int? vendorId,
        [FromQuery] int? currencyId, CancellationToken ct)
        => Ok(await mediator.Send(new Features.VendorPayments.GetVendorPaymentSuggestionsQuery(payDate, dueThrough, vendorId, currencyId), ct));

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
