using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.TrackTypes;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/track-types")]
[Authorize]
[RequiresCapability("CAP-EXT-KANBAN")]
public class TrackTypesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TrackTypeResponseModel>>> GetTrackTypes()
    {
        var result = await mediator.Send(new GetTrackTypesQuery());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TrackTypeResponseModel>> GetTrackType(int id)
    {
        var result = await mediator.Send(new GetTrackTypeByIdQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/custom-fields")]
    public async Task<ActionResult<List<CustomFieldDefinitionModel>>> GetCustomFieldDefinitions(int id)
    {
        var result = await mediator.Send(new GetCustomFieldDefinitionsQuery(id));
        return Ok(result);
    }

    [HttpPut("{id:int}/custom-fields")]
    public async Task<ActionResult<List<CustomFieldDefinitionModel>>> UpdateCustomFieldDefinitions(
        int id, UpdateCustomFieldDefinitionsCommand command)
    {
        var cmd = command with { TrackTypeId = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }
}
