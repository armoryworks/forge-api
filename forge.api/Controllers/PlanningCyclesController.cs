using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.PlanningCycles;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/planning-cycles")]
[Authorize(Roles = "Admin,Manager,PM,Engineer,ProductionWorker")]
[RequiresCapability("CAP-PLAN-MRP")]
public class PlanningCyclesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PlanningCycleListItemModel>>> GetPlanningCycles()
    {
        var result = await mediator.Send(new GetPlanningCyclesQuery());
        return Ok(result);
    }

    [HttpGet("current")]
    public async Task<ActionResult<PlanningCycleDetailResponseModel>> GetCurrentPlanningCycle()
    {
        var result = await mediator.Send(new GetCurrentPlanningCycleQuery());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PlanningCycleDetailResponseModel>> GetPlanningCycle(int id)
    {
        var result = await mediator.Send(new GetPlanningCycleByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<PlanningCycleListItemModel>> CreatePlanningCycle(CreatePlanningCycleRequestModel request)
    {
        var result = await mediator.Send(new CreatePlanningCycleCommand(
            request.Name, request.StartDate, request.EndDate, request.Goals, request.DurationDays));
        return CreatedAtAction(nameof(GetPlanningCycle), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdatePlanningCycle(int id, UpdatePlanningCycleRequestModel request)
    {
        await mediator.Send(new UpdatePlanningCycleCommand(id, request.Name, request.StartDate, request.EndDate, request.Goals));
        return NoContent();
    }

    [HttpPost("{id:int}/activate")]
    public async Task<IActionResult> ActivatePlanningCycle(int id)
    {
        await mediator.Send(new ActivatePlanningCycleCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/complete")]
    public async Task<ActionResult> CompletePlanningCycle(int id, CompletePlanningCycleRequestModel request)
    {
        var newCycleId = await mediator.Send(new CompletePlanningCycleCommand(id, request.RolloverIncomplete));
        return Ok(new { newCycleId });
    }

    [HttpPost("{id:int}/entries")]
    public async Task<IActionResult> CommitJobToCycle(int id, CommitJobRequestModel request)
    {
        await mediator.Send(new CommitJobToCycleCommand(id, request.JobId));
        return NoContent();
    }

    [HttpDelete("{id:int}/entries/{jobId:int}")]
    public async Task<IActionResult> RemoveJobFromCycle(int id, int jobId)
    {
        await mediator.Send(new RemoveJobFromCycleCommand(id, jobId));
        return NoContent();
    }

    [HttpPut("{id:int}/entries/order")]
    public async Task<IActionResult> UpdateEntryOrder(int id, UpdateEntryOrderRequestModel request)
    {
        await mediator.Send(new UpdateEntryOrderCommand(id, request.Items));
        return NoContent();
    }

    [HttpPost("{id:int}/entries/{jobId:int}/complete")]
    public async Task<IActionResult> CompleteEntry(int id, int jobId)
    {
        await mediator.Send(new CompleteEntryCommand(id, jobId));
        return NoContent();
    }
}
