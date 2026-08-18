using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.Barcodes;
using Forge.Core.Enums;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/barcodes")]
[Authorize]
// internal barcode identity is cross-entity infrastructure (parts, jobs, bins, shipments); GS1 GTINs are the separately gated CAP-MD-GS1 surface
[CapabilityBootstrap]
public class BarcodesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<IActionResult> GetEntityBarcodes(
        [FromQuery] BarcodeEntityType entityType,
        [FromQuery] int entityId,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(new GetEntityBarcodesQuery(entityType, entityId), cancellationToken);
        return Ok(result);
    }

    [HttpPost("regenerate")]
    public async Task<IActionResult> Regenerate(
        [FromBody] RegenerateBarcodeRequestModel request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new RegenerateBarcodeCommand(request.EntityType, request.EntityId, request.NaturalIdentifier),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Add a manual alternate barcode (manufacturer UPC, vendor SKU, legacy label) on top of the
    /// entity's auto-assigned code. The value must be globally unique.</summary>
    [HttpPost]
    public async Task<IActionResult> AddManual(
        [FromBody] AddManualBarcodeRequestModel request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new AddManualBarcodeCommand(request.EntityType, request.EntityId, request.Value),
            cancellationToken);
        return Ok(result);
    }

    /// <summary>Remove a manually-added alternate barcode. The auto-assigned code cannot be removed.</summary>
    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RemoveManual(int id, CancellationToken cancellationToken)
    {
        await mediator.Send(new RemoveManualBarcodeCommand(id), cancellationToken);
        return NoContent();
    }
}

public record RegenerateBarcodeRequestModel(BarcodeEntityType EntityType, int EntityId, string NaturalIdentifier);

public record AddManualBarcodeRequestModel(BarcodeEntityType EntityType, int EntityId, string Value);
