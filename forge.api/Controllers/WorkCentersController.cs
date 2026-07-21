using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Scheduling;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/work-centers")]
[Authorize]
[RequiresCapability("CAP-MD-WORKCENTERS")]
public class WorkCentersController(IMediator mediator) : ControllerBase
{
    // Read is open to Engineer as well as Admin/Manager: defining a routing/operation
    // requires reading the work centers operations route through (core MRP/engineering
    // read). Writes stay Admin/Manager. Mirrors the Quality ECO/Gage method-level split.
    [HttpGet]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<List<WorkCenterResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetWorkCentersQuery());
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<WorkCenterResponseModel>> Create([FromBody] CreateWorkCenterRequest request)
    {
        var result = await mediator.Send(new CreateWorkCenterCommand(
            request.Name, request.Code, request.Description,
            request.DailyCapacityHours, request.EfficiencyPercent,
            request.NumberOfMachines, request.LaborCostPerHour,
            request.BurdenRatePerHour, request.AssetId,
            request.CompanyLocationId, request.SortOrder));
        return CreatedAtAction(nameof(GetAll), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<WorkCenterResponseModel>> Update(int id, [FromBody] UpdateWorkCenterRequest request)
    {
        var result = await mediator.Send(new UpdateWorkCenterCommand(
            id, request.Name, request.Code, request.Description,
            request.DailyCapacityHours, request.EfficiencyPercent,
            request.NumberOfMachines, request.LaborCostPerHour,
            request.BurdenRatePerHour, request.IsActive,
            request.AssetId, request.CompanyLocationId, request.SortOrder));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteWorkCenterCommand(id));
        return NoContent();
    }
}

public record CreateWorkCenterRequest(
    string Name,
    string Code,
    string? Description,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent,
    int NumberOfMachines,
    decimal LaborCostPerHour,
    decimal BurdenRatePerHour,
    int? AssetId,
    int? CompanyLocationId,
    int SortOrder);

public record UpdateWorkCenterRequest(
    string Name,
    string Code,
    string? Description,
    decimal DailyCapacityHours,
    decimal EfficiencyPercent,
    int NumberOfMachines,
    decimal LaborCostPerHour,
    decimal BurdenRatePerHour,
    bool IsActive,
    int? AssetId,
    int? CompanyLocationId,
    int SortOrder);
