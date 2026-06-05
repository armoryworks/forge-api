using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.PurchaseOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/purchase-orders")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-P2P-PO")]
public class PurchaseOrdersController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-broad / WU-22 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /purchase-orders?page=1&amp;pageSize=25&amp;sort=createdAt&amp;order=desc&amp;q=PO-001&amp;vendorId=4&amp;status=Draft&amp;dateFrom=2025-01-01&amp;dateTo=2025-12-31</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?vendorId=&amp;jobId=&amp;status=</c>
    /// form continues to work — those params are bound directly on the
    /// PurchaseOrderListQuery model. Existing UI callers that don't pass any
    /// query params get the standard default (page 1, 25 records,
    /// createdAt desc).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<PurchaseOrderListItemModel>>> GetPurchaseOrders(
        [FromQuery] PurchaseOrderListQuery query,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetPurchaseOrdersQuery(query), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PurchaseOrderDetailResponseModel>> GetPurchaseOrder(int id)
    {
        var result = await mediator.Send(new GetPurchaseOrderByIdQuery(id));
        return Ok(result);
    }

    [HttpPut("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<PurchaseOrderDetailResponseModel>> UpdatePurchaseOrderLine(
        int id, int lineId, UpdateOrderLineRequestModel request)
    {
        var result = await mediator.Send(new UpdatePurchaseOrderLineCommand(id, lineId, request));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PurchaseOrderListItemModel>> CreatePurchaseOrder(CreatePurchaseOrderRequestModel request)
    {
        var result = await mediator.Send(new CreatePurchaseOrderCommand(
            request.VendorId, request.JobId, request.Notes, request.Lines));
        return CreatedAtAction(nameof(GetPurchaseOrder), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(PurchaseOrder))]
    public async Task<IActionResult> UpdatePurchaseOrder(int id, UpdatePurchaseOrderRequestModel request)
    {
        await mediator.Send(new UpdatePurchaseOrderCommand(id, request.Notes, request.ExpectedDeliveryDate));
        return NoContent();
    }

    [HttpPost("{id:int}/submit")]
    public async Task<IActionResult> SubmitPurchaseOrder(int id)
    {
        await mediator.Send(new SubmitPurchaseOrderCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/acknowledge")]
    public async Task<IActionResult> AcknowledgePurchaseOrder(int id, AcknowledgePurchaseOrderRequestModel request)
    {
        await mediator.Send(new AcknowledgePurchaseOrderCommand(id, request.ExpectedDeliveryDate));
        return NoContent();
    }

    [HttpPost("{id:int}/receive")]
    public async Task<IActionResult> ReceiveItems(int id, ReceiveItemsRequestModel request)
    {
        await mediator.Send(new ReceiveItemsCommand(
            id,
            request.Lines,
            request.ActualFreight,
            request.FreightAllocationMethod));
        return NoContent();
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelPurchaseOrder(int id)
    {
        await mediator.Send(new CancelPurchaseOrderCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/close")]
    public async Task<IActionResult> ClosePurchaseOrder(int id)
    {
        await mediator.Send(new ClosePurchaseOrderCommand(id));
        return NoContent();
    }

    // Phase 3 / WU-14 / H3 — short-close a partially-received PO. The /close
    // endpoint 409s on anything that isn't fully received; this lets AP /
    // Procurement close the PO when the remainder won't be received (vendor
    // backorder cancelled, item discontinued). Reason is required for audit.
    [HttpPost("{id:int}/short-close")]
    [Authorize(Roles = "Admin,Manager,OfficeManager,Procurement")]
    public async Task<ActionResult<PurchaseOrderDetailResponseModel>> ShortClosePurchaseOrder(
        int id, [FromBody] ShortClosePurchaseOrderRequestModel request)
    {
        if (request is null || string.IsNullOrWhiteSpace(request.Reason))
            return BadRequest(new { errors = new { reason = new[] { "Reason is required." } } });

        await mediator.Send(new ShortClosePurchaseOrderCommand(id, request.Reason));
        var updated = await mediator.Send(new GetPurchaseOrderByIdQuery(id));
        return Ok(updated);
    }

    [HttpGet("calendar")]
    public async Task<ActionResult<List<PoCalendarResponseModel>>> GetForCalendar(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to)
    {
        var result = await mediator.Send(new GetPurchaseOrdersForCalendarQuery(from, to));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(PurchaseOrder))]
    public async Task<IActionResult> DeletePurchaseOrder(int id)
    {
        await mediator.Send(new DeletePurchaseOrderCommand(id));
        return NoContent();
    }

    // ── Blanket PO Releases ──────────────────────────────────────────────

    [HttpGet("{id:int}/releases")]
    public async Task<ActionResult<List<PurchaseOrderReleaseResponseModel>>> GetReleases(int id)
    {
        var result = await mediator.Send(new GetPurchaseOrderReleasesQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/releases")]
    public async Task<ActionResult<PurchaseOrderReleaseResponseModel>> CreateRelease(
        int id, [FromBody] CreatePurchaseOrderReleaseRequestModel request)
    {
        var result = await mediator.Send(new CreatePurchaseOrderReleaseCommand(id, request));
        return Created($"/api/v1/purchase-orders/{id}/releases/{result.ReleaseNumber}", result);
    }

    [HttpPatch("{id:int}/releases/{releaseNum:int}")]
    public async Task<IActionResult> UpdateRelease(
        int id, int releaseNum, [FromBody] UpdatePurchaseOrderReleaseRequestModel request)
    {
        await mediator.Send(new UpdatePurchaseOrderReleaseCommand(id, releaseNum, request));
        return NoContent();
    }

    // AI-assisted review of a manual unit-price override (forge#6). Also gated on
    // the AI capability; the handler degrades gracefully when the model is offline.
    [HttpPost("price-variance-review")]
    [RequiresCapability("CAP-EXT-AI-ASSISTANT")]
    public async Task<ActionResult<ReviewPriceOverrideResponseModel>> ReviewPriceOverride(
        [FromBody] ReviewPriceOverrideRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new ReviewPriceOverrideQuery(request), ct));
}
