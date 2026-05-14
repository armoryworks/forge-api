using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Api.Features.Assets;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/assets")]
[Authorize(Roles = "Admin,Manager,Engineer")]
[RequiresCapability("CAP-MD-ASSETS")]
public class AssetsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AssetResponseModel>>> GetAssets(
        [FromQuery] AssetType? type,
        [FromQuery] AssetStatus? status,
        [FromQuery] string? search)
    {
        var result = await mediator.Send(new GetAssetsQuery(type, status, search));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<AssetResponseModel>> CreateAsset([FromBody] CreateAssetRequestModel request)
    {
        var result = await mediator.Send(new CreateAssetCommand(request));
        return Created($"/api/v1/assets/{result.Id}", result);
    }

    [HttpPatch("{id:int}")]
    public async Task<ActionResult<AssetResponseModel>> UpdateAsset(int id, [FromBody] UpdateAssetRequestModel request)
    {
        var result = await mediator.Send(new UpdateAssetCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAsset(int id)
    {
        await mediator.Send(new DeleteAssetCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetAssetActivity(int id)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Asset", id));
        return Ok(result);
    }

    [HttpGet("{id:int}/maintenance/logs")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<ActionResult<List<MaintenanceLogListItemResponseModel>>> GetAssetMaintenanceLogs(int id)
    {
        var result = await mediator.Send(new GetAssetMaintenanceLogsQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/maintenance")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<ActionResult<List<MaintenanceScheduleResponseModel>>> GetAssetMaintenanceSchedules(int id)
    {
        var result = await mediator.Send(new GetMaintenanceSchedulesQuery(id));
        return Ok(result);
    }

    [HttpGet("maintenance")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<ActionResult<List<MaintenanceScheduleResponseModel>>> GetAllMaintenanceSchedules()
    {
        var result = await mediator.Send(new GetMaintenanceSchedulesQuery(null));
        return Ok(result);
    }

    [HttpPost("{id:int}/maintenance")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<ActionResult<MaintenanceScheduleResponseModel>> CreateMaintenanceSchedule(
        int id, [FromBody] CreateMaintenanceScheduleRequestModel request)
    {
        var command = new CreateMaintenanceScheduleCommand(request with { AssetId = id });
        var result = await mediator.Send(command);
        return Created($"/api/v1/assets/{id}/maintenance/{result.Id}", result);
    }

    [HttpPost("maintenance/{scheduleId:int}/log")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<ActionResult<MaintenanceLogResponseModel>> LogMaintenance(
        int scheduleId, [FromBody] LogMaintenanceRequestModel request)
    {
        var result = await mediator.Send(new LogMaintenanceCommand(scheduleId, request));
        return Created($"/api/v1/assets/maintenance/{scheduleId}/log/{result.Id}", result);
    }

    [HttpDelete("maintenance/{scheduleId:int}")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<IActionResult> DeleteMaintenanceSchedule(int scheduleId)
    {
        await mediator.Send(new DeleteMaintenanceScheduleCommand(scheduleId));
        return NoContent();
    }

    // ─── Machine Hours ───

    [HttpPatch("{id:int}/hours")]
    public async Task<ActionResult<AssetResponseModel>> UpdateMachineHours(
        int id, [FromBody] UpdateMachineHoursRequestModel request)
    {
        var result = await mediator.Send(new UpdateMachineHoursCommand(id, request));
        return Ok(result);
    }

    // ─── Downtime Logging ───

    [HttpGet("{id:int}/downtime")]
    [RequiresCapability("CAP-MAINT-BREAKDOWN")]
    public async Task<ActionResult<List<DowntimeLogResponseModel>>> GetAssetDowntime(int id)
    {
        var result = await mediator.Send(new GetDowntimeLogsQuery(id));
        return Ok(result);
    }

    [HttpGet("downtime")]
    [RequiresCapability("CAP-MAINT-BREAKDOWN")]
    public async Task<ActionResult<List<DowntimeLogResponseModel>>> GetAllDowntime()
    {
        var result = await mediator.Send(new GetDowntimeLogsQuery(null));
        return Ok(result);
    }

    [HttpPost("{id:int}/downtime")]
    [RequiresCapability("CAP-MAINT-BREAKDOWN")]
    public async Task<ActionResult<DowntimeLogResponseModel>> CreateDowntimeLog(
        int id, [FromBody] CreateDowntimeLogRequestModel request)
    {
        var result = await mediator.Send(new CreateDowntimeLogCommand(request with { AssetId = id }));
        return Created($"/api/v1/assets/{id}/downtime/{result.Id}", result);
    }

    [HttpPatch("downtime/{id:int}/end")]
    [RequiresCapability("CAP-MAINT-BREAKDOWN")]
    public async Task<ActionResult<DowntimeLogResponseModel>> EndDowntimeLog(int id)
    {
        var result = await mediator.Send(new EndDowntimeLogCommand(id));
        return Ok(result);
    }

    // ─── Maintenance Job Linking ───

    [HttpPost("maintenance/{scheduleId:int}/create-job")]
    [RequiresCapability("CAP-MAINT-PM")]
    public async Task<ActionResult<object>> CreateMaintenanceJob(int scheduleId)
    {
        var jobId = await mediator.Send(new CreateMaintenanceJobCommand(scheduleId));
        return Created($"/api/v1/jobs/{jobId}", new { jobId });
    }
}
