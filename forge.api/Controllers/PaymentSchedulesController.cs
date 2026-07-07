using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.PaymentSchedules;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Quote/order pre-payment schedules (S2). The schedule is authored on the
/// quote and re-linked to the sales order at conversion, so it is addressable
/// from both documents. Milestone actions (mark-paid / waive / generate-invoice)
/// address the milestone row directly.
/// Note: generate-invoice is ⚡ accounting-bounded — standalone mode only.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-QUOTE")]
public class PaymentSchedulesController(IMediator mediator) : ControllerBase
{
    [HttpGet("quotes/{quoteId:int}/payment-schedule")]
    public async Task<ActionResult<PaymentScheduleResponseModel>> GetForQuote(int quoteId)
    {
        var result = await mediator.Send(new GetPaymentScheduleQuery(quoteId, null));
        return result is null ? NotFound() : Ok(result);
    }

    [HttpGet("orders/{salesOrderId:int}/payment-schedule")]
    public async Task<ActionResult<PaymentScheduleResponseModel>> GetForOrder(int salesOrderId)
    {
        var result = await mediator.Send(new GetPaymentScheduleQuery(null, salesOrderId));
        return result is null ? NotFound() : Ok(result);
    }

    [HttpPut("quotes/{quoteId:int}/payment-schedule")]
    public async Task<ActionResult<PaymentScheduleResponseModel>> Upsert(
        int quoteId, UpsertPaymentScheduleRequestModel request)
    {
        var result = await mediator.Send(new UpsertPaymentScheduleCommand(quoteId, request.Milestones));
        return Ok(result);
    }

    [HttpPost("payment-milestones/{id:int}/mark-paid")]
    public async Task<ActionResult<PaymentMilestoneResponseModel>> MarkPaid(
        int id, MarkMilestonePaidRequestModel request)
    {
        var result = await mediator.Send(new MarkMilestonePaidCommand(id, request.PaidAmount, request.PaidReference));
        return Ok(result);
    }

    [HttpPost("payment-milestones/{id:int}/waive")]
    public async Task<IActionResult> Waive(int id)
    {
        await mediator.Send(new WaiveMilestoneCommand(id));
        return NoContent();
    }

    [HttpPost("payment-milestones/{id:int}/generate-invoice")]
    public async Task<ActionResult<InvoiceListItemModel>> GenerateInvoice(int id)
    {
        var result = await mediator.Send(new GenerateMilestoneInvoiceCommand(id));
        return Created($"/api/v1/invoices/{result.Id}", result);
    }
}
