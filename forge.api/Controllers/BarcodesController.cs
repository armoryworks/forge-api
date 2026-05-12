using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.Barcodes;
using Forge.Core.Enums;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/barcodes")]
[Authorize]
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
}

public record RegenerateBarcodeRequestModel(BarcodeEntityType EntityType, int EntityId, string NaturalIdentifier);
