using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-SO")]
public class SalesOrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SalesOrderListItemModel>>> GetSalesOrders(
        [FromQuery] int? customerId,
        [FromQuery] SalesOrderStatus? status)
    {
        var result = await mediator.Send(new GetSalesOrdersQuery(customerId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> GetSalesOrder(int id)
    {
        var result = await mediator.Send(new GetSalesOrderByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<SalesOrderListItemModel>> CreateSalesOrder(CreateSalesOrderRequestModel request)
    {
        var result = await mediator.Send(new CreateSalesOrderCommand(
            request.CustomerId, request.QuoteId, request.ShippingAddressId,
            request.BillingAddressId, request.CreditTerms, request.RequestedDeliveryDate,
            request.CustomerPO, request.Notes, request.TaxRate, request.Lines));
        return CreatedAtAction(nameof(GetSalesOrder), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(SalesOrder))]
    public async Task<IActionResult> UpdateSalesOrder(int id, UpdateSalesOrderRequestModel request)
    {
        await mediator.Send(new UpdateSalesOrderCommand(
            id, request.ShippingAddressId, request.BillingAddressId,
            request.CreditTerms, request.RequestedDeliveryDate,
            request.CustomerPO, request.Notes, request.TaxRate));
        return NoContent();
    }

    [HttpPost("{id:int}/lines")]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> AddSalesOrderLine(int id, CreateSalesOrderLineModel request)
    {
        var result = await mediator.Send(new AddSalesOrderLineCommand(id, request));
        return Ok(result);
    }

    [HttpPut("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> UpdateSalesOrderLine(
        int id, int lineId, UpdateOrderLineRequestModel request)
    {
        var result = await mediator.Send(new UpdateSalesOrderLineCommand(id, lineId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> DeleteSalesOrderLine(int id, int lineId)
    {
        var result = await mediator.Send(new DeleteSalesOrderLineCommand(id, lineId));
        return Ok(result);
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmSalesOrder(int id)
    {
        await mediator.Send(new ConfirmSalesOrderCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/cancel")]
    public async Task<IActionResult> CancelSalesOrder(int id)
    {
        await mediator.Send(new CancelSalesOrderCommand(id));
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(SalesOrder))]
    public async Task<IActionResult> DeleteSalesOrder(int id)
    {
        await mediator.Send(new DeleteSalesOrderCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/schedule")]
    public async Task<ActionResult<List<ScheduleMilestoneModel>>> GetSchedule(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderScheduleQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/documents")]
    public async Task<ActionResult<List<FileAttachmentResponseModel>>> GetDocuments(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderDocumentsQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/invoices")]
    public async Task<ActionResult<List<SalesOrderInvoiceModel>>> GetInvoices(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderInvoicesQuery(id), ct);
        return Ok(result);
    }
}
