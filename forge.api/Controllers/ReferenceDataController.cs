using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Authorization;
using Forge.Api.Features.ReferenceData;
using Forge.Core.Models;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/reference-data")]
[Authorize]
// reference_data powers dropdowns on every screen; gating it blanks the whole app
[CapabilityBootstrap]
public class ReferenceDataController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ReferenceDataGroupResponseModel>>> GetGroups()
    {
        var result = await mediator.Send(new GetReferenceDataGroupsQuery());
        return Ok(result);
    }

    // Kiosks (shop floor) need to read reference-data groups (clock event types,
    // hold types, etc.) before any worker has authenticated. Accept the kiosk
    // device token as a fallback credential on this read-only endpoint.
    [HttpGet("{groupCode}")]
    [AllowAnonymous]
    [KioskTerminalAuth]
    public async Task<ActionResult<List<ReferenceDataResponseModel>>> GetByGroup(string groupCode)
    {
        var result = await mediator.Send(new GetReferenceDataByGroupQuery(groupCode));
        return Ok(result);
    }
}
