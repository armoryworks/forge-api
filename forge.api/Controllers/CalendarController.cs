using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Calendar;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// compliance-calendar (cluster A). The overlay calendar's taxonomy/read surface.
/// Per-group visibility is enforced in the handlers (A-2), so this is [Authorize] only;
/// see blocking-questions inventory re: a dedicated calendar capability.
/// </summary>
[ApiController]
[Route("api/v1/calendar")]
[Authorize]
public class CalendarController(IMediator mediator) : ControllerBase
{
    /// <summary>Super-Groups (with Event-Types) the current user may see — the layer list.</summary>
    [HttpGet("super-groups")]
    public async Task<ActionResult<List<CalendarSuperGroupResponseModel>>> GetSuperGroups()
        => Ok(await mediator.Send(new GetCalendarSuperGroupsQuery()));
}
