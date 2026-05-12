using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.ScheduledTasks;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/scheduled-tasks")]
[Authorize(Roles = "Admin")]
public class ScheduledTasksController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ScheduledTaskResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetScheduledTasksQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ScheduledTaskResponseModel>> Create(CreateScheduledTaskRequestModel request)
    {
        var result = await mediator.Send(new CreateScheduledTaskCommand(
            request.Name, request.Description, request.TrackTypeId,
            request.InternalProjectTypeId, request.AssigneeId, request.CronExpression));
        return CreatedAtAction(nameof(GetAll), result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ScheduledTaskResponseModel>> Update(int id, UpdateScheduledTaskCommand command)
    {
        var cmd = command with { Id = id };
        var result = await mediator.Send(cmd);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteScheduledTaskCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/run")]
    public async Task<ActionResult<int>> Run(int id)
    {
        var jobId = await mediator.Send(new RunScheduledTaskCommand(id));
        return Ok(new { jobId });
    }
}
