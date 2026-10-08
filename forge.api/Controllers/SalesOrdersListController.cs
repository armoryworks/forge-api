using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// SalesOrders list/read surface under <c>/api/v1/sales-orders</c>: the paged
/// list and the by-id row, both over the SalesOrder entity, one row per order.
///
/// Mutations (POST/PUT/PATCH/DELETE) and the detail/schedule/documents/invoices
/// endpoints live on the <c>/api/v1/orders</c> surface; see
/// <see cref="SalesOrdersController"/>. This controller is read-only.
/// </summary>
[ApiController]
[Route("api/v1/sales-orders")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-SO")]
public class SalesOrdersListController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Paged sales-order list. Standard WU-17 envelope:
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
    /// Single sales order in the list-row shape. Returns 404 if the SalesOrder
    /// id does not exist.
    ///
    /// For the full SalesOrder detail (with lines, shipments, returns, tax),
    /// callers should use <c>GET /api/v1/orders/{id}</c>.
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
