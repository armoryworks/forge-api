using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// CAP-QC-RECALL — lot-based recalls. Initiating a recall walks the lot-consumption genealogy
/// forward from the recalled lot to every affected produced lot, quarantines matching on-hand,
/// resolves the customers/shipments that received affected lots (SO-line granularity), and
/// stores it as an immutable snapshot.
/// </summary>
[ApiController]
[Route("api/v1/recalls")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-QC-RECALL")]
public class RecallsController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<RecallDetailResponseModel>> Initiate(
        [FromBody] InitiateRecallRequestModel request)
    {
        var result = await mediator.Send(new InitiateRecallCommand(request));
        return Created($"/api/v1/recalls/{result.Id}", result);
    }

    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<List<RecallResponseModel>>> List([FromQuery] RecallStatus? status)
    {
        var result = await mediator.Send(new GetRecallsQuery(status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<RecallDetailResponseModel>> Detail(int id)
    {
        var result = await mediator.Send(new GetRecallDetailQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<ActionResult<RecallDetailResponseModel>> Resolve(
        int id, [FromBody] ResolveRecallRequestModel request)
    {
        var result = await mediator.Send(new ResolveRecallCommand(id, request));
        return Ok(result);
    }
}
