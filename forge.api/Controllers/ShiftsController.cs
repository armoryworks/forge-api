using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Authorization;
using Forge.Api.Capabilities;
using Forge.Api.Features.Scheduling;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/shifts")]
[Authorize(Roles = PlanningRoles.Plan)]
[RequiresCapability("CAP-HR-SHIFTS")]
public class ShiftsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ShiftResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetShiftsQuery());
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ShiftResponseModel>> Create([FromBody] CreateShiftRequest request)
    {
        var result = await mediator.Send(new CreateShiftCommand(
            request.Name, request.StartTime, request.EndTime, request.BreakMinutes, request.WorkCenters));
        return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ShiftResponseModel>> Update(int id, [FromBody] UpdateShiftRequest request)
    {
        var result = await mediator.Send(new UpdateShiftCommand(
            id, request.Name, request.StartTime, request.EndTime,
            request.BreakMinutes, request.IsActive, request.WorkCenters));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteShiftCommand(id));
        return NoContent();
    }
}

public record CreateShiftRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, List<ShiftWorkCenterModel>? WorkCenters = null);

public record UpdateShiftRequest(
    string Name, TimeOnly StartTime, TimeOnly EndTime, int BreakMinutes, bool IsActive,
    List<ShiftWorkCenterModel>? WorkCenters = null);
