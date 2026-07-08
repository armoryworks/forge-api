using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// S4c — staged production/shipment/payment scheduling on sales orders. Stages
/// are the user-owned editable layer over the advisory derived backward-scheduling
/// timeline. Split routes: order-scoped collection actions under
/// <c>orders/{salesOrderId}/stages</c>, single-stage actions under
/// <c>sales-order-stages/{id}</c>. Kept on a dedicated controller to avoid churn
/// on the large <see cref="SalesOrdersController"/>.
/// </summary>
[ApiController]
[Route("api/v1")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-SO")]
public class SalesOrderStagesController(IMediator mediator) : ControllerBase
{
    [HttpGet("orders/{salesOrderId:int}/stages")]
    public async Task<ActionResult<SalesOrderStagesResponseModel>> GetStages(int salesOrderId, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderStagesQuery(salesOrderId), ct);
        return Ok(result);
    }

    [HttpPost("orders/{salesOrderId:int}/stages/activate")]
    public async Task<ActionResult<SalesOrderStagesResponseModel>> Activate(int salesOrderId, CancellationToken ct)
    {
        var result = await mediator.Send(new ActivateStagedScheduleCommand(salesOrderId), ct);
        return Ok(result);
    }

    [HttpPost("orders/{salesOrderId:int}/stages")]
    public async Task<ActionResult<SalesOrderStageResponseModel>> Create(
        int salesOrderId, UpsertSalesOrderStageRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpsertSalesOrderStageCommand(
            null, salesOrderId, request.Name, request.Sequence,
            request.PlannedProductionComplete, request.PlannedShipDate,
            request.Notes, request.PaymentMilestoneId), ct);
        return CreatedAtAction(nameof(GetStages), new { salesOrderId }, result);
    }

    [HttpPut("sales-order-stages/{id:int}")]
    public async Task<ActionResult<SalesOrderStageResponseModel>> Update(
        int id, UpsertSalesOrderStageRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpsertSalesOrderStageCommand(
            id, null, request.Name, request.Sequence,
            request.PlannedProductionComplete, request.PlannedShipDate,
            request.Notes, request.PaymentMilestoneId), ct);
        return Ok(result);
    }

    [HttpDelete("sales-order-stages/{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteSalesOrderStageCommand(id), ct);
        return NoContent();
    }

    [HttpPut("sales-order-stages/{id:int}/lines")]
    public async Task<ActionResult<SalesOrderStageResponseModel>> AssignLines(
        int id, AssignStageLinesRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new AssignStageLinesCommand(id, request.Lines), ct);
        return Ok(result);
    }

    [HttpPut("sales-order-stages/{id:int}/lots")]
    public async Task<ActionResult<SalesOrderStageResponseModel>> AssignLots(
        int id, AssignLotsToStageRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new AssignLotsToStageCommand(id, request.LotIds), ct);
        return Ok(result);
    }

    [HttpPost("sales-order-stages/{id:int}/complete")]
    public async Task<ActionResult<SalesOrderStageResponseModel>> Complete(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new CompleteStageCommand(id), ct);
        return Ok(result);
    }

    [HttpPost("sales-order-stages/{id:int}/ship")]
    public async Task<ActionResult<SalesOrderStageResponseModel>> Ship(
        int id, ShipStageRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new ShipStageCommand(id, request.ShipmentId), ct);
        return Ok(result);
    }
}
