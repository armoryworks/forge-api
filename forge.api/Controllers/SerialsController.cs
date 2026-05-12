using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Serials;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/serials")]
[Authorize(Roles = "Admin,Manager,Engineer")]
[RequiresCapability("CAP-INV-SERIALS")]
public class SerialsController(IMediator mediator) : ControllerBase
{
    [HttpGet("part/{partId:int}")]
    public async Task<ActionResult<List<SerialNumberResponseModel>>> GetPartSerials(
        int partId, [FromQuery] SerialNumberStatus? status)
    {
        var result = await mediator.Send(new GetPartSerialsQuery(partId, status));
        return Ok(result);
    }

    [HttpPost("part/{partId:int}")]
    public async Task<ActionResult<SerialNumberResponseModel>> CreateSerialNumber(
        int partId, [FromBody] CreateSerialNumberRequestModel request)
    {
        var result = await mediator.Send(new CreateSerialNumberCommand(partId, request));
        return Created($"/api/v1/serials/{result.Id}", result);
    }

    [HttpGet("{serialValue}/genealogy")]
    public async Task<ActionResult<SerialGenealogyResponseModel>> GetGenealogy(string serialValue)
    {
        var result = await mediator.Send(new GetSerialGenealogyQuery(serialValue));
        return Ok(result);
    }

    [HttpPost("{id:int}/transfer")]
    public async Task<IActionResult> TransferSerial(int id, [FromBody] TransferSerialRequestModel request)
    {
        await mediator.Send(new TransferSerialCommand(id, request));
        return NoContent();
    }

    [HttpGet("{id:int}/history")]
    public async Task<ActionResult<List<SerialHistoryResponseModel>>> GetSerialHistory(int id)
    {
        var result = await mediator.Send(new GetSerialHistoryQuery(id));
        return Ok(result);
    }
}
