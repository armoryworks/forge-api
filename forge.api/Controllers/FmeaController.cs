using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/fmeas")]
[Authorize]
[RequiresCapability("CAP-QC-FMEA")]
public class FmeaController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<FmeaResponseModel>>> GetFmeas(
        [FromQuery] FmeaType? type, [FromQuery] int? partId, [FromQuery] FmeaStatus? status)
    {
        var result = await mediator.Send(new GetFmeasQuery(type, partId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<FmeaResponseModel>> GetFmea(int id)
    {
        var result = await mediator.Send(new GetFmeaQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<FmeaResponseModel>> CreateFmea(
        [FromBody] CreateFmeaRequestModel request)
    {
        var result = await mediator.Send(new CreateFmeaCommand(request));
        return Created($"/api/v1/fmeas/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<FmeaResponseModel>> UpdateFmea(
        int id, [FromBody] UpdateFmeaRequestModel request)
    {
        var result = await mediator.Send(new UpdateFmeaCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/items")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<FmeaItemResponseModel>> AddItem(
        int id, [FromBody] CreateFmeaItemRequestModel request)
    {
        var result = await mediator.Send(new AddFmeaItemCommand(id, request));
        return Created($"/api/v1/fmeas/{id}/items/{result.Id}", result);
    }

    [HttpPut("{id:int}/items/{itemId:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<FmeaItemResponseModel>> UpdateItem(
        int id, int itemId, [FromBody] UpdateFmeaItemRequestModel request)
    {
        var result = await mediator.Send(new UpdateFmeaItemCommand(id, itemId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/items/{itemId:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<IActionResult> DeleteItem(int id, int itemId)
    {
        await mediator.Send(new DeleteFmeaItemCommand(id, itemId));
        return NoContent();
    }

    [HttpPost("{id:int}/items/{itemId:int}/action")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<FmeaItemResponseModel>> RecordAction(
        int id, int itemId, [FromBody] RecordFmeaActionRequestModel request)
    {
        var result = await mediator.Send(new RecordFmeaActionCommand(id, itemId, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/items/{itemId:int}/link-capa")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<IActionResult> LinkToCapa(int id, int itemId, [FromQuery] int capaId)
    {
        await mediator.Send(new LinkFmeaToCapaCommand(id, itemId, capaId));
        return NoContent();
    }

    [HttpGet("high-rpn")]
    public async Task<ActionResult<List<FmeaItemResponseModel>>> GetHighRpnItems(
        [FromQuery] int threshold = 200)
    {
        var result = await mediator.Send(new GetHighRpnItemsQuery(threshold));
        return Ok(result);
    }

    [HttpGet("{id:int}/risk-summary")]
    public async Task<ActionResult<FmeaRiskSummaryResponseModel>> GetRiskSummary(int id)
    {
        var result = await mediator.Send(new GetFmeaRiskSummaryQuery(id));
        return Ok(result);
    }
}
