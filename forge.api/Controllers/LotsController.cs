using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Lots;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/lots")]
[Authorize(Roles = "Admin,Manager,Engineer,ProductionWorker")]
[RequiresCapability("CAP-INV-LOTS")]
public class LotsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LotRecordResponseModel>>> GetLotRecords(
        [FromQuery] int? partId,
        [FromQuery] int? jobId,
        [FromQuery] string? search)
    {
        var result = await mediator.Send(new GetLotRecordsQuery(partId, jobId, search));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<LotRecordResponseModel>> CreateLotRecord(
        [FromBody] CreateLotRecordRequestModel request)
    {
        var result = await mediator.Send(new CreateLotRecordCommand(request));
        return Created($"/api/v1/lots/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<LotRecordResponseModel>> UpdateLotRecord(
        int id, [FromBody] UpdateLotRecordRequestModel request)
    {
        var result = await mediator.Send(new UpdateLotRecordCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteLotRecord(int id)
    {
        await mediator.Send(new DeleteLotRecordCommand(id));
        return NoContent();
    }

    [HttpGet("{lotNumber}/trace")]
    public async Task<ActionResult<LotTraceabilityResponseModel>> GetTraceability(string lotNumber)
    {
        var result = await mediator.Send(new GetLotTraceabilityQuery(lotNumber));
        return Ok(result);
    }
}
