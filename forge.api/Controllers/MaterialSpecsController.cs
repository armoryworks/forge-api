using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Parts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/material-specs")]
[Authorize(Roles = "Admin,Manager,Engineer")]
[RequiresCapability("CAP-MD-PARTS")]
public class MaterialSpecsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MaterialSpecResponseModel>>> List(
        [FromQuery] bool includeRetired = false, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetMaterialSpecsQuery(includeRetired), ct));

    [HttpPut("{id:int}")]
    public async Task<ActionResult<MaterialSpecResponseModel>> Update(
        int id, [FromBody] UpdateMaterialSpecRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateMaterialSpecCommand(id, request), ct));

    [HttpPost("{id:int}/retire")]
    public async Task<ActionResult<MaterialSpecResponseModel>> Retire(int id, CancellationToken ct)
        => Ok(await mediator.Send(new SetMaterialSpecActiveCommand(id, false), ct));

    [HttpPost("{id:int}/reactivate")]
    public async Task<ActionResult<MaterialSpecResponseModel>> Reactivate(int id, CancellationToken ct)
        => Ok(await mediator.Send(new SetMaterialSpecActiveCommand(id, true), ct));

    [HttpPost]
    public async Task<ActionResult<ReferenceDataResponseModel>> Create(
        [FromBody] CreateMaterialSpecRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateMaterialSpecCommand(request), ct);
        return Created($"/api/v1/reference-data/{CreateMaterialSpecHandler.GroupCode}", result);
    }
}
