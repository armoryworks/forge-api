using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Events;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/events")]
[Authorize]
[RequiresCapability("CAP-MD-EMPLOYEES")]
public class EventsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EventResponseModel>>> GetEvents(
        [FromQuery] DateTimeOffset? from,
        [FromQuery] DateTimeOffset? to,
        [FromQuery] string? eventType)
    {
        var result = await mediator.Send(new GetEventsQuery(from, to, eventType));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EventResponseModel>> GetEvent(int id)
    {
        var result = await mediator.Send(new GetEventByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<EventResponseModel>> CreateEvent([FromBody] EventRequestModel request)
    {
        var result = await mediator.Send(new CreateEventCommand(
            request.Title, request.Description, request.StartTime, request.EndTime,
            request.Location, request.EventType, request.IsRequired, request.AttendeeUserIds));
        return Created($"/api/v1/events/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<EventResponseModel>> UpdateEvent(int id, [FromBody] EventRequestModel request)
    {
        var result = await mediator.Send(new UpdateEventCommand(
            id, request.Title, request.Description, request.StartTime, request.EndTime,
            request.Location, request.EventType, request.IsRequired, request.AttendeeUserIds));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteEvent(int id)
    {
        await mediator.Send(new DeleteEventCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/respond")]
    public async Task<IActionResult> RespondToEvent(int id, [FromBody] RespondToEventRequestModel request)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        await mediator.Send(new RespondToEventCommand(id, userId, request.Status));
        return NoContent();
    }

    [HttpGet("upcoming")]
    public async Task<ActionResult<List<EventResponseModel>>> GetUpcomingEvents()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var result = await mediator.Send(new GetUpcomingEventsForUserQuery(userId));
        return Ok(result);
    }

    [HttpGet("upcoming/{userId:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<EventResponseModel>>> GetUpcomingEventsForUser(int userId)
    {
        var result = await mediator.Send(new GetUpcomingEventsForUserQuery(userId));
        return Ok(result);
    }
}

public record RespondToEventRequestModel(string Status);
