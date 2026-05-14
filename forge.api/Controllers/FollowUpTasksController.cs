using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.FollowUpTasks;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/follow-up-tasks")]
[Authorize]
public class FollowUpTasksController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);

    [HttpGet]
    public async Task<ActionResult<List<FollowUpTaskResponseModel>>> GetTasks(
        [FromQuery] FollowUpStatus? status)
    {
        var result = await mediator.Send(new GetFollowUpTasksQuery(GetUserId(), status));
        return Ok(result);
    }

    [HttpPost("{id:int}/complete")]
    public async Task<IActionResult> Complete(int id)
    {
        await mediator.Send(new CompleteFollowUpTaskCommand(id, GetUserId()));
        return NoContent();
    }

    [HttpPost("{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(int id)
    {
        await mediator.Send(new DismissFollowUpTaskCommand(id, GetUserId()));
        return NoContent();
    }
}
