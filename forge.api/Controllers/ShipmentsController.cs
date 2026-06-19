using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.Shipments;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/shipments")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-SHIP")]
public class ShipmentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ShipmentListItemModel>>> GetShipments(
        [FromQuery] int? salesOrderId,
        [FromQuery] ShipmentStatus? status)
    {
        var result = await mediator.Send(new GetShipmentsQuery(salesOrderId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ShipmentDetailResponseModel>> GetShipment(int id)
    {
        var result = await mediator.Send(new GetShipmentByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ShipmentListItemModel>> CreateShipment(CreateShipmentRequestModel request)
    {
        var result = await mediator.Send(new CreateShipmentCommand(
            request.SalesOrderId, request.ShippingAddressId, request.Carrier,
            request.TrackingNumber, request.ShippingCost, request.Weight,
            request.Notes, request.Lines, request.CarrierId));
        return CreatedAtAction(nameof(GetShipment), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(Shipment))]
    public async Task<IActionResult> UpdateShipment(int id, UpdateShipmentRequestModel request)
    {
        await mediator.Send(new UpdateShipmentCommand(
            id, request.Carrier, request.TrackingNumber,
            request.ShippingCost, request.Weight, request.Notes));
        return NoContent();
    }

    [HttpPost("{id:int}/ship")]
    public async Task<IActionResult> ShipShipment(int id, [FromBody] ShipShipmentRequestModel? request = null)
    {
        await mediator.Send(new ShipShipmentCommand(id, request?.ScanCode));
        return NoContent();
    }

    [HttpGet("{id:int}/packing-slip")]
    public async Task<IActionResult> GetPackingSlip(int id)
    {
        var pdf = await mediator.Send(new GeneratePackingSlipPdfQuery(id));
        return File(pdf, "application/pdf", $"packing-slip-{id}.pdf");
    }

    [HttpGet("{id:int}/bill-of-lading")]
    public async Task<IActionResult> GetBillOfLading(int id)
    {
        var pdf = await mediator.Send(new GenerateBillOfLadingPdfQuery(id));
        return File(pdf, "application/pdf", $"bill-of-lading-{id}.pdf");
    }

    [HttpPost("{id:int}/deliver")]
    public async Task<IActionResult> DeliverShipment(int id)
    {
        await mediator.Send(new DeliverShipmentCommand(id));
        return NoContent();
    }

    // ── Shipping Rates / Labels / Tracking ──

    [HttpPost("{id:int}/rates")]
    public async Task<ActionResult<List<ShippingRate>>> GetShippingRates(int id, GetShippingRatesRequestModel request)
    {
        var result = await mediator.Send(new GetShippingRatesQuery(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/label")]
    public async Task<ActionResult<ShippingLabel>> CreateShippingLabel(int id, CreateShippingLabelRequestModel request)
    {
        var result = await mediator.Send(new CreateShippingLabelCommand(id, request.CarrierId));
        return Ok(result);
    }

    [HttpGet("{id:int}/tracking")]
    public async Task<ActionResult<ShipmentTracking?>> GetShipmentTracking(int id)
    {
        var result = await mediator.Send(new GetShipmentTrackingQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/pickup")]
    public async Task<ActionResult<PickupConfirmation>> SchedulePickup(
        int id, [FromBody] SchedulePickupRequestModel? request = null)
    {
        var result = await mediator.Send(new SchedulePickupCommand(
            id, request?.ReadyTime, request?.CloseTime, request?.Instructions));
        return Ok(result);
    }

    [HttpPost("{id:int}/pickup/cancel")]
    public async Task<IActionResult> CancelPickup(int id)
    {
        await mediator.Send(new CancelPickupCommand(id));
        return NoContent();
    }

    [HttpPost("validate-address")]
    public async Task<ActionResult<AddressValidationResponseModel>> ValidateAddress(ValidateAddressRequestModel request)
    {
        var result = await mediator.Send(new ValidateShippingAddressCommand(request));
        return Ok(result);
    }

    // ── Packages ──

    [HttpGet("{id:int}/packages")]
    public async Task<ActionResult<List<ShipmentPackageResponseModel>>> GetPackages(int id)
    {
        var result = await mediator.Send(new GetShipmentPackagesQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/packages")]
    public async Task<ActionResult<ShipmentPackageResponseModel>> AddPackage(int id, AddShipmentPackageCommand command)
    {
        var cmd = command with { ShipmentId = id };
        var result = await mediator.Send(cmd);
        return CreatedAtAction(nameof(GetPackages), new { id }, result);
    }

    [HttpPatch("{id:int}/packages/{packageId:int}")]
    public async Task<ActionResult<ShipmentPackageResponseModel>> UpdatePackage(int id, int packageId, UpdateShipmentPackageCommand command)
    {
        var cmd = command with { ShipmentId = id, PackageId = packageId };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpDelete("{id:int}/packages/{packageId:int}")]
    public async Task<IActionResult> RemovePackage(int id, int packageId)
    {
        await mediator.Send(new RemoveShipmentPackageCommand(id, packageId));
        return NoContent();
    }
}
