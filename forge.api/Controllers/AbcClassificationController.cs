using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/inventory/abc")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer")]
[RequiresCapability("CAP-PLAN-ABC")]
public class AbcClassificationController(IMediator mediator) : ControllerBase
{
    [HttpPost("run")]
    public async Task<ActionResult<AbcClassificationRunResponseModel>> RunClassification([FromBody] AbcClassificationParametersModel parameters)
    {
        var result = await mediator.Send(new RunAbcClassificationCommand(parameters));
        return Created($"/api/v1/inventory/abc/runs/{result.Id}", result);
    }

    [HttpGet("runs")]
    public async Task<ActionResult<List<AbcClassificationRunResponseModel>>> GetRuns()
    {
        var result = await mediator.Send(new GetAbcClassificationRunsQuery());
        return Ok(result);
    }

    [HttpGet("runs/{runId:int}/results")]
    public async Task<ActionResult<List<AbcClassificationResultResponseModel>>> GetRunResults(int runId)
    {
        var result = await mediator.Send(new GetAbcClassificationResultsQuery(runId));
        return Ok(result);
    }

    [HttpGet("summary")]
    public async Task<ActionResult<AbcClassificationSummaryResponseModel>> GetSummary()
    {
        var result = await mediator.Send(new GetAbcSummaryQuery());
        return Ok(result);
    }

    [HttpPost("runs/{runId:int}/apply")]
    public async Task<IActionResult> ApplyRun(int runId)
    {
        await mediator.Send(new ApplyAbcClassificationCommand(runId));
        return NoContent();
    }
}
