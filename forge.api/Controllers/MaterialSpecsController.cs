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
    [HttpPost]
    public async Task<ActionResult<ReferenceDataResponseModel>> Create(
        [FromBody] CreateMaterialSpecRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateMaterialSpecCommand(request), ct);
        return Created($"/api/v1/reference-data/{CreateMaterialSpecHandler.GroupCode}", result);
    }
}
