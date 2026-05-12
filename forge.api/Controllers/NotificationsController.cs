using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Notifications;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/notifications")]
[Authorize]
[RequiresCapability("CAP-CROSS-NOTIFICATIONS")]
public class NotificationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<object>> GetNotifications()
    {
        var result = await mediator.Send(new GetNotificationsQuery());
        return Ok(new { data = result });
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult> UpdateNotification(int id, UpdateNotificationRequestModel request)
    {
        await mediator.Send(new UpdateNotificationCommand(id, request.IsRead, request.IsPinned, request.IsDismissed));
        return NoContent();
    }

    [HttpPost("mark-all-read")]
    public async Task<ActionResult> MarkAllRead()
    {
        await mediator.Send(new MarkAllReadCommand());
        return NoContent();
    }

    [HttpPost("dismiss-all")]
    public async Task<ActionResult> DismissAll()
    {
        await mediator.Send(new DismissAllCommand());
        return NoContent();
    }
}
