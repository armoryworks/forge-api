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

    /// <summary>The current user's saved views (personal + role-default), optionally scoped.</summary>
    [HttpGet("saved-views")]
    public async Task<ActionResult<List<CalendarSavedViewResponseModel>>> GetSavedViews([FromQuery] string? scope)
        => Ok(await mediator.Send(new GetCalendarSavedViewsQuery(scope)));

    /// <summary>Save the current overlay-layer selection as a personal view.</summary>
    [HttpPost("saved-views")]
    public async Task<ActionResult<CalendarSavedViewResponseModel>> CreateSavedView([FromBody] CalendarSavedViewRequestModel request)
    {
        var result = await mediator.Send(new CreateCalendarSavedViewCommand(
            request.Name, request.Scope, request.SelectedSuperGroupIds, request.SelectedEventTypeIds));
        return Created($"/api/v1/calendar/saved-views/{result.Id}", result);
    }

    /// <summary>Delete one of the current user's own saved views.</summary>
    [HttpDelete("saved-views/{id:int}")]
    public async Task<IActionResult> DeleteSavedView(int id)
    {
        await mediator.Send(new DeleteCalendarSavedViewCommand(id));
        return NoContent();
    }
}
