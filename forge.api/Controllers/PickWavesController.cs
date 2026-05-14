using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Shipping;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/pick-waves")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer,ProductionWorker")]
[RequiresCapability("CAP-O2C-PICKPACK")]
public class PickWavesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PickWaveResponseModel>>> GetWaves(
        [FromQuery] PickWaveStatus? status,
        [FromQuery] int? assignedToId)
    {
        var result = await mediator.Send(new GetPickWavesQuery(status, assignedToId));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PickWaveResponseModel>> GetWave(int id)
    {
        var result = await mediator.Send(new GetPickWaveQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PickWaveResponseModel>> CreateWave([FromBody] CreatePickWaveRequestModel request)
    {
        var result = await mediator.Send(new CreatePickWaveCommand(request));
        return Created($"/api/v1/pick-waves/{result.Id}", result);
    }

    [HttpPost("auto-generate")]
    public async Task<ActionResult<PickWaveResponseModel>> AutoGenerateWave([FromBody] AutoWaveParametersModel parameters)
    {
        var result = await mediator.Send(new AutoGeneratePickWaveCommand(parameters));
        return Created($"/api/v1/pick-waves/{result.Id}", result);
    }

    [HttpPost("{id:int}/release")]
    public async Task<IActionResult> ReleaseWave(int id)
    {
        await mediator.Send(new ReleasePickWaveCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/lines/{lineId:int}/confirm")]
    public async Task<IActionResult> ConfirmPickLine(int id, int lineId, [FromBody] ConfirmPickLineRequestModel request)
    {
        await mediator.Send(new ConfirmPickLineCommand(id, lineId, request));
        return NoContent();
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> CompleteWave(int id)
    {
        await mediator.Send(new CompletePickWaveCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/print")]
    public async Task<ActionResult<PickWaveResponseModel>> PrintPickList(int id)
    {
        var result = await mediator.Send(new PrintPickListQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetPickListPdf(int id)
    {
        var pdf = await mediator.Send(new GeneratePickListPdfQuery(id));
        return File(pdf, "application/pdf", $"pick-list-{id}.pdf");
    }
}
