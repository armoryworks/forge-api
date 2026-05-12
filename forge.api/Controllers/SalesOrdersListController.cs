using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// SalesOrders list/read surface — Phase 3 F1 partial / WU-18.
///
/// The SalesOrders list endpoint is a query-side projection over Jobs (the
/// canonical transactional entity). A "sales order" is a Job at "Order
/// Confirmed" stage or downstream (materials_ordered, materials_received,
/// in_production, qc_review, shipped, invoiced_sent, payment_received).
///
/// Mutations (POST/PUT/PATCH/DELETE) and the existing detail/schedule/documents/
/// invoices endpoints continue to live on the legacy <c>/api/v1/orders</c>
/// surface — see <see cref="SalesOrdersController"/>. This controller is
/// read-only and exposes only the list + by-id projections that Phase 1 found
/// missing under the <c>/sales-orders</c> route.
///
/// Full unification (dropping the SalesOrder entity, migrating references) is
/// a future architectural pass (F1-broad).
/// </summary>
[ApiController]
[Route("api/v1/sales-orders")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-SO")]
public class SalesOrdersListController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Paged Job-projected sales-order list. Standard WU-17 envelope:
    /// <c>{ items, totalCount, page, pageSize }</c>.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<SalesOrderListItemModel>>> GetSalesOrders(
        [FromQuery] SalesOrderListQuery query,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrdersListQuery(query), ct);
        return Ok(result);
    }

    /// <summary>
    /// Single Job-as-SO projection. Returns 404 if the Job id does not exist or
    /// is not at an SO-stage (order_confirmed and downstream).
    ///
    /// For the full SalesOrder detail (with lines, shipments, returns, tax),
    /// callers should use <c>GET /api/v1/orders/{id}</c> against the legacy
    /// SalesOrder entity. This endpoint exists for parity with the list shape
    /// and to satisfy the Phase 1 gap.
    /// </summary>
    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesOrderListItemModel>> GetSalesOrder(
        int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderProjectionByIdQuery(id), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }
}
