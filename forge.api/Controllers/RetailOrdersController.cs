using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.RetailBuyers;
using Forge.Api.Features.RetailOrders;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Consumer orders and the buyers behind them.
///
/// <para>Separate from <c>SalesOrdersController</c> because the entry contract
/// genuinely differs — no quote, no credit terms, no customer PO, and a buyer
/// plus ship-to instead of a customer id. The orders themselves are ordinary
/// sales orders once created, so everything downstream (shipments, invoices,
/// reporting) is served by the existing sales-order endpoints.</para>
/// </summary>
[ApiController]
[Route("api/v1/retail-orders")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-RETAIL")]
public class RetailOrdersController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Create a consumer order. Idempotent on (channel, externalOrderNumber):
    /// a replay of an already-imported order returns 200 with the existing
    /// order rather than 409, so a connector retrying a partially-failed batch
    /// does not need to probe first.
    /// </summary>
    [HttpPost]
    public async Task<ActionResult<SalesOrderListItemModel>> CreateRetailOrder(
        [FromBody] CreateRetailOrderRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateRetailOrderCommand(model), ct);

        return result.Created
            ? CreatedAtAction(nameof(CreateRetailOrder), new { id = result.Order.Id }, result.Order)
            : Ok(result.Order);
    }

    [HttpGet("buyers")]
    public async Task<ActionResult<PagedResponse<RetailBuyerResponseModel>>> GetBuyers(
        [FromQuery] RetailBuyerListQuery query, CancellationToken ct)
    {
        var result = await mediator.Send(new GetRetailBuyersQuery(query), ct);
        return Ok(result);
    }

    /// <summary>
    /// Erasure on request. Scrubs the buyer's identifying columns and the frozen
    /// ship-to on each of their orders, keeping the orders and their totals.
    /// Admin-only: it is irreversible and it is the response to a legal request,
    /// not routine housekeeping.
    /// </summary>
    [HttpPost("buyers/{id:int}/purge-pii")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<PurgeRetailBuyerPiiResult>> PurgeBuyerPii(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new PurgeRetailBuyerPiiCommand(id), ct);
        return Ok(result);
    }
}
