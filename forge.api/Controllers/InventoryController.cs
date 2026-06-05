using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/inventory")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer,ProductionWorker")]
[RequiresCapability("CAP-INV-CORE")]
public class InventoryController(IMediator mediator) : ControllerBase
{
    [HttpGet("locations")]
    public async Task<ActionResult<List<StorageLocationResponseModel>>> GetLocationTree()
    {
        var result = await mediator.Send(new GetLocationTreeQuery());
        return Ok(result);
    }

    [HttpGet("locations/bins")]
    public async Task<ActionResult<PagedResponse<StorageLocationFlatResponseModel>>> GetBinLocations(
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetBinLocationsQuery(search, page, pageSize), ct);
        return Ok(result);
    }

    [HttpPost("locations")]
    public async Task<ActionResult<StorageLocationResponseModel>> CreateLocation([FromBody] CreateStorageLocationRequestModel request)
    {
        var result = await mediator.Send(new CreateStorageLocationCommand(request));
        return Created($"/api/v1/inventory/locations", result);
    }

    [HttpPut("locations/{id:int}")]
    public async Task<ActionResult<StorageLocationResponseModel>> UpdateLocation(
        int id, [FromBody] UpdateStorageLocationRequestModel request)
    {
        var result = await mediator.Send(new UpdateStorageLocationCommand(id, request));
        return Ok(result);
    }

    [HttpGet("locations/{locationId:int}/contents")]
    public async Task<ActionResult<List<BinContentResponseModel>>> GetBinContents(int locationId)
    {
        var result = await mediator.Send(new GetBinContentsQuery(locationId));
        return Ok(result);
    }

    [HttpPost("bin-contents")]
    public async Task<ActionResult<BinContentResponseModel>> PlaceBinContent([FromBody] PlaceBinContentRequestModel request)
    {
        var result = await mediator.Send(new PlaceBinContentCommand(request));
        return Created($"/api/v1/inventory/bin-contents/{result.Id}", result);
    }

    [HttpGet("parts")]
    public async Task<ActionResult<List<InventoryPartSummaryResponseModel>>> GetPartInventory([FromQuery] string? search)
    {
        var result = await mediator.Send(new GetPartInventoryQuery(search));
        return Ok(result);
    }

    [HttpGet("movements")]
    public async Task<ActionResult<List<BinMovementResponseModel>>> GetMovements(
        [FromQuery] int? locationId,
        [FromQuery] string? entityType,
        [FromQuery] int? entityId,
        [FromQuery] int take = 100)
    {
        var result = await mediator.Send(new GetMovementsQuery(locationId, entityType, entityId, take));
        return Ok(result);
    }

    [HttpDelete("locations/{id:int}")]
    public async Task<IActionResult> DeleteLocation(int id)
    {
        await mediator.Send(new DeleteStorageLocationCommand(id));
        return NoContent();
    }

    [HttpDelete("bin-contents/{id:int}")]
    public async Task<IActionResult> RemoveBinContent(int id)
    {
        await mediator.Send(new RemoveBinContentCommand(id));
        return NoContent();
    }

    [HttpGet("low-stock")]
    public async Task<ActionResult<List<LowStockAlertModel>>> GetLowStockAlerts()
    {
        var result = await mediator.Send(new GetLowStockAlertsQuery());
        return Ok(result);
    }

    // ── Receiving ──

    [HttpPost("receive")]
    public async Task<ActionResult<ReceivingRecordResponseModel>> ReceiveGoods(
        [FromBody] ReceivePurchaseOrderRequestModel request)
    {
        var result = await mediator.Send(new ReceivePurchaseOrderCommand(request));
        return Created($"/api/v1/inventory/receiving-history", result);
    }

    [HttpGet("receiving-history")]
    public async Task<ActionResult<List<ReceivingRecordResponseModel>>> GetReceivingHistory(
        [FromQuery] int? purchaseOrderId,
        [FromQuery] int? partId,
        [FromQuery] int take = 50)
    {
        var result = await mediator.Send(new GetReceivingHistoryQuery(purchaseOrderId, partId, take));
        return Ok(result);
    }

    // ── Stock Operations ──

    [HttpPost("transfer")]
    [RequiresCapability("CAP-INV-MULTILOC")]
    public async Task<IActionResult> TransferStock([FromBody] TransferStockRequestModel request)
    {
        await mediator.Send(new TransferStockCommand(request));
        return NoContent();
    }

    [HttpPost("adjust")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> AdjustStock([FromBody] AdjustStockRequestModel request)
    {
        await mediator.Send(new AdjustStockCommand(request));
        return NoContent();
    }

    // Manual inventory override (forge-api#4) — set on-hand for an existing part
    // at a location, creating the bin content when none exists (opening stock).
    [HttpPost("set-on-hand")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-INV-ADJUST")]
    public async Task<IActionResult> SetOnHandQuantity([FromBody] SetOnHandQuantityRequestModel request)
    {
        await mediator.Send(new SetOnHandQuantityCommand(request));
        return NoContent();
    }

    // ── Cycle Counts ──

    [HttpGet("cycle-counts")]
    [RequiresCapability("CAP-INV-CYCLECOUNT")]
    public async Task<ActionResult<List<CycleCountResponseModel>>> GetCycleCounts(
        [FromQuery] int? locationId,
        [FromQuery] string? status)
    {
        var result = await mediator.Send(new GetCycleCountsQuery(locationId, status));
        return Ok(result);
    }

    [HttpPost("cycle-counts")]
    [RequiresCapability("CAP-INV-CYCLECOUNT")]
    public async Task<ActionResult<CycleCountResponseModel>> CreateCycleCount(
        [FromBody] CreateCycleCountRequestModel request)
    {
        var result = await mediator.Send(new CreateCycleCountCommand(request));
        return Created($"/api/v1/inventory/cycle-counts/{result.Id}", result);
    }

    [HttpPut("cycle-counts/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-INV-CYCLECOUNT")]
    public async Task<IActionResult> UpdateCycleCount(int id, [FromBody] UpdateCycleCountRequestModel request)
    {
        await mediator.Send(new UpdateCycleCountCommand(id, request));
        return NoContent();
    }

    // ── Reservations ──

    [HttpGet("reservations")]
    [RequiresCapability("CAP-INV-RESERVE")]
    public async Task<ActionResult<List<ReservationResponseModel>>> GetReservations(
        [FromQuery] int? partId,
        [FromQuery] int? jobId)
    {
        var result = await mediator.Send(new GetReservationsQuery(partId, jobId));
        return Ok(result);
    }

    [HttpPost("reservations")]
    [RequiresCapability("CAP-INV-RESERVE")]
    public async Task<ActionResult<ReservationResponseModel>> CreateReservation(
        [FromBody] CreateReservationRequestModel request)
    {
        var result = await mediator.Send(new CreateReservationCommand(request));
        return Created($"/api/v1/inventory/reservations/{result.Id}", result);
    }

    [HttpDelete("reservations/{id:int}")]
    [RequiresCapability("CAP-INV-RESERVE")]
    public async Task<IActionResult> ReleaseReservation(int id)
    {
        await mediator.Send(new ReleaseReservationCommand(id));
        return NoContent();
    }

    // ── Inspection ──

    [HttpGet("pending-inspection")]
    [RequiresCapability("CAP-QC-INSPECTION")]
    public async Task<ActionResult<List<PendingInspectionItem>>> GetPendingInspections()
    {
        var result = await mediator.Send(new GetPendingInspectionsQuery());
        return Ok(result);
    }

    [HttpPost("inspect/{receivingRecordId:int}")]
    [RequiresCapability("CAP-QC-INSPECTION")]
    public async Task<IActionResult> RecordInspectionResult(int receivingRecordId, [FromBody] InspectionResultRequestModel data)
    {
        await mediator.Send(new RecordInspectionResultCommand(receivingRecordId, data));
        return NoContent();
    }

    [HttpPost("inspect/{receivingRecordId:int}/waive")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-QC-INSPECTION")]
    public async Task<IActionResult> WaiveInspection(int receivingRecordId)
    {
        await mediator.Send(new WaiveInspectionCommand(receivingRecordId));
        return NoContent();
    }

    // ── Units of Measure ──

    [HttpGet("uom")]
    [RequiresCapability("CAP-MD-UOM")]
    public async Task<ActionResult<List<UomResponseModel>>> GetUnitsOfMeasure([FromQuery] string? category)
    {
        var result = await mediator.Send(new GetUnitsOfMeasureQuery(category));
        return Ok(result);
    }

    [HttpPost("uom")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-MD-UOM")]
    public async Task<ActionResult<UomResponseModel>> CreateUnitOfMeasure([FromBody] CreateUomRequestModel data)
    {
        var result = await mediator.Send(new CreateUnitOfMeasureCommand(data));
        return Created($"/api/v1/inventory/uom/{result.Id}", result);
    }

    [HttpPut("uom/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-MD-UOM")]
    public async Task<ActionResult<UomResponseModel>> UpdateUnitOfMeasure(int id, [FromBody] CreateUomRequestModel data)
    {
        var result = await mediator.Send(new UpdateUnitOfMeasureCommand(id, data));
        return Ok(result);
    }

    [HttpGet("uom/conversions")]
    [RequiresCapability("CAP-MD-UOM")]
    public async Task<ActionResult<List<UomConversionResponseModel>>> GetUomConversions([FromQuery] int? partId)
    {
        var result = await mediator.Send(new GetUomConversionsQuery(partId));
        return Ok(result);
    }

    [HttpPost("uom/conversions")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-MD-UOM")]
    public async Task<ActionResult<UomConversionResponseModel>> CreateUomConversion([FromBody] CreateUomConversionRequestModel data)
    {
        var result = await mediator.Send(new CreateUomConversionCommand(data));
        return Created($"/api/v1/inventory/uom/conversions/{result.Id}", result);
    }

    [HttpGet("uom/convert")]
    [RequiresCapability("CAP-MD-UOM")]
    public async Task<ActionResult<ConvertQuantityResult>> ConvertQuantity(
        [FromQuery] int fromUomId, [FromQuery] int toUomId,
        [FromQuery] decimal quantity, [FromQuery] int? partId)
    {
        var result = await mediator.Send(new ConvertQuantityQuery(fromUomId, toUomId, quantity, partId));
        return Ok(result);
    }

    // ── ATP (Available-to-Promise) ──────────────────────────────────────

    [HttpGet("atp/{partId:int}")]
    [RequiresCapability("CAP-PLAN-ATP")]
    public async Task<ActionResult<AtpResult>> GetAtp(int partId, [FromQuery] decimal quantity = 1)
    {
        var result = await mediator.Send(new GetAtpForPartQuery(partId, quantity));
        return Ok(result);
    }

    [HttpGet("atp/{partId:int}/timeline")]
    [RequiresCapability("CAP-PLAN-ATP")]
    public async Task<ActionResult<List<AtpBucket>>> GetAtpTimeline(
        int partId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to)
    {
        var result = await mediator.Send(new GetAtpTimelineQuery(partId, from, to));
        return Ok(result);
    }
}
