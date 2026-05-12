using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/quality")]
[Authorize]
[RequiresCapability("CAP-QC-NCR")]
public class NcrCapaController(IMediator mediator) : ControllerBase
{
    // ── NCR ──────────────────────────────────────────────────

    [HttpGet("ncrs")]
    public async Task<ActionResult<List<NcrResponseModel>>> GetNcrs(
        [FromQuery] NcrType? type,
        [FromQuery] NcrStatus? status,
        [FromQuery] int? partId,
        [FromQuery] int? jobId,
        [FromQuery] int? vendorId,
        [FromQuery] int? customerId,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo)
    {
        var result = await mediator.Send(new GetNcrsQuery(type, status, partId, jobId, vendorId, customerId, dateFrom, dateTo));
        return Ok(result);
    }

    [HttpPost("ncrs")]
    public async Task<ActionResult<NcrResponseModel>> CreateNcr([FromBody] CreateNcrRequestModel request)
    {
        var result = await mediator.Send(new CreateNcrCommand(request));
        return CreatedAtAction(nameof(GetNcrById), new { id = result.Id }, result);
    }

    [HttpGet("ncrs/{id:int}")]
    public async Task<ActionResult<NcrResponseModel>> GetNcrById(int id)
    {
        var result = await mediator.Send(new GetNcrByIdQuery(id));
        return Ok(result);
    }

    [HttpPatch("ncrs/{id:int}")]
    public async Task<IActionResult> UpdateNcr(int id, [FromBody] UpdateNcrRequestModel request)
    {
        await mediator.Send(new UpdateNcrCommand(id, request));
        return NoContent();
    }

    [HttpPost("ncrs/{id:int}/disposition")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DispositionNcr(int id, [FromBody] DispositionNcrRequestModel request)
    {
        await mediator.Send(new DispositionNcrCommand(id, request));
        return NoContent();
    }

    [HttpPost("ncrs/{id:int}/create-capa")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CapaResponseModel>> CreateCapaFromNcr(int id, [FromBody] CreateCapaFromNcrRequestModel request)
    {
        var result = await mediator.Send(new CreateCapaFromNcrCommand(id, request.OwnerId));
        return CreatedAtAction(nameof(GetCapaById), new { id = result.Id }, result);
    }

    // ── CAPA ─────────────────────────────────────────────────

    [HttpGet("capas")]
    public async Task<ActionResult<List<CapaResponseModel>>> GetCapas(
        [FromQuery] CapaStatus? status,
        [FromQuery] CapaType? type,
        [FromQuery] int? ownerId,
        [FromQuery] int? priority,
        [FromQuery] DateTimeOffset? dueDateFrom,
        [FromQuery] DateTimeOffset? dueDateTo)
    {
        var result = await mediator.Send(new GetCapasQuery(status, type, ownerId, priority, dueDateFrom, dueDateTo));
        return Ok(result);
    }

    [HttpPost("capas")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CapaResponseModel>> CreateCapa([FromBody] CreateCapaRequestModel request)
    {
        var result = await mediator.Send(new CreateCapaCommand(request));
        return CreatedAtAction(nameof(GetCapaById), new { id = result.Id }, result);
    }

    [HttpGet("capas/{id:int}")]
    public async Task<ActionResult<CapaResponseModel>> GetCapaById(int id)
    {
        var result = await mediator.Send(new GetCapaByIdQuery(id));
        return Ok(result);
    }

    [HttpPatch("capas/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> UpdateCapa(int id, [FromBody] UpdateCapaRequestModel request)
    {
        await mediator.Send(new UpdateCapaCommand(id, request));
        return NoContent();
    }

    [HttpPost("capas/{id:int}/advance")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CapaResponseModel>> AdvanceCapaPhase(int id)
    {
        var result = await mediator.Send(new AdvanceCapaPhaseCommand(id));
        return Ok(result);
    }

    [HttpGet("capas/{id:int}/tasks")]
    public async Task<ActionResult<List<CapaTaskResponseModel>>> GetCapaTasks(int id)
    {
        var result = await mediator.Send(new GetCapaTasksQuery(id));
        return Ok(result);
    }

    [HttpPost("capas/{id:int}/tasks")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CapaTaskResponseModel>> CreateCapaTask(int id, [FromBody] CreateCapaTaskRequestModel request)
    {
        var result = await mediator.Send(new CreateCapaTaskCommand(id, request));
        return CreatedAtAction(nameof(GetCapaTasks), new { id }, result);
    }

    [HttpPatch("capas/{id:int}/tasks/{taskId:int}")]
    public async Task<IActionResult> UpdateCapaTask(int id, int taskId, [FromBody] UpdateCapaTaskRequestModel request)
    {
        await mediator.Send(new UpdateCapaTaskCommand(id, taskId, request));
        return NoContent();
    }
}
