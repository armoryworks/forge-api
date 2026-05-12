using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.Payments;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Standalone mode: full CRUD. Integrated mode: read-only.
/// </summary>
[ApiController]
[Route("api/v1/payments")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-CASH")]
public class PaymentsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-broad / WU-22 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /payments?page=1&amp;pageSize=25&amp;sort=paymentDate&amp;order=desc&amp;q=PMT-001&amp;customerId=4&amp;paymentMethod=Check&amp;dateFrom=2025-01-01&amp;dateTo=2025-12-31</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?customerId=</c> form continues to
    /// work. Existing UI callers that don't pass any query params get the
    /// standard default (page 1, 25 records, paymentDate desc).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<PaymentListItemModel>>> GetPayments(
        [FromQuery] PaymentListQuery query,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetPaymentsQuery(query), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentDetailResponseModel>> GetPayment(int id)
    {
        var result = await mediator.Send(new GetPaymentByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PaymentListItemModel>> CreatePayment(CreatePaymentRequestModel request)
    {
        var result = await mediator.Send(new CreatePaymentCommand(
            request.CustomerId, request.Method, request.Amount,
            request.PaymentDate, request.ReferenceNumber, request.Notes,
            request.Applications));
        return CreatedAtAction(nameof(GetPayment), new { id = result.Id }, result);
    }

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(Payment))]
    public async Task<IActionResult> DeletePayment(int id)
    {
        await mediator.Send(new DeletePaymentCommand(id));
        return NoContent();
    }
}
