using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Mrp;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/mrp")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-PLAN-MRP")]
public class MrpController(IMediator mediator) : ControllerBase
{
    // === MRP Runs ===

    [HttpGet("runs")]
    public async Task<ActionResult<List<MrpRunResponseModel>>> GetRuns()
    {
        var result = await mediator.Send(new GetMrpRunsQuery());
        return Ok(result);
    }

    [HttpGet("runs/{id:int}")]
    public async Task<ActionResult<MrpRunResponseModel>> GetRun(int id)
    {
        var result = await mediator.Send(new GetMrpRunDetailQuery(id));
        return Ok(result);
    }

    [HttpPost("runs")]
    public async Task<ActionResult<MrpRunResponseModel>> ExecuteRun([FromBody] ExecuteMrpRunRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var result = await mediator.Send(new ExecuteMrpRunCommand(
            request.RunType,
            request.PlanningHorizonDays,
            request.PartIds,
            userId
        ));
        return CreatedAtAction(nameof(GetRun), new { id = result.Id }, result);
    }

    [HttpPost("runs/simulate")]
    public async Task<ActionResult<MrpRunResponseModel>> SimulateRun([FromBody] ExecuteMrpRunRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var result = await mediator.Send(new SimulateMrpRunCommand(
            MrpRunType.Simulation,
            request.PlanningHorizonDays,
            request.PartIds,
            userId
        ));
        return Ok(result);
    }

    // === Planned Orders ===

    [HttpGet("planned-orders")]
    public async Task<ActionResult<List<MrpPlannedOrderResponseModel>>> GetPlannedOrders(
        [FromQuery] int? mrpRunId,
        [FromQuery] MrpPlannedOrderStatus? status)
    {
        var result = await mediator.Send(new GetPlannedOrdersQuery(mrpRunId, status));
        return Ok(result);
    }

    [HttpPatch("planned-orders/{id:int}")]
    public async Task<IActionResult> UpdatePlannedOrder(int id, [FromBody] UpdatePlannedOrderRequest request)
    {
        await mediator.Send(new UpdatePlannedOrderCommand(id, request.IsFirmed, request.Notes));
        return NoContent();
    }

    [HttpPost("planned-orders/{id:int}/release")]
    public async Task<ActionResult<ReleasePlannedOrderResult>> ReleasePlannedOrder(int id)
    {
        var result = await mediator.Send(new ReleasePlannedOrderCommand(id));
        return Ok(result);
    }

    [HttpPost("planned-orders/bulk-release")]
    public async Task<ActionResult<List<ReleasePlannedOrderResult>>> BulkReleasePlannedOrders([FromBody] BulkReleaseRequest request)
    {
        var result = await mediator.Send(new BulkReleasePlannedOrdersCommand(request.Ids));
        return Ok(result);
    }

    [HttpDelete("planned-orders/{id:int}")]
    public async Task<IActionResult> DeletePlannedOrder(int id)
    {
        await mediator.Send(new DeletePlannedOrderCommand(id));
        return NoContent();
    }

    // === Exceptions ===

    [HttpGet("exceptions")]
    public async Task<ActionResult<List<MrpExceptionResponseModel>>> GetExceptions(
        [FromQuery] int? mrpRunId,
        [FromQuery] bool? unresolvedOnly)
    {
        var result = await mediator.Send(new GetMrpExceptionsQuery(mrpRunId, unresolvedOnly));
        return Ok(result);
    }

    [HttpPost("exceptions/{id:int}/resolve")]
    public async Task<IActionResult> ResolveException(int id, [FromBody] ResolveExceptionRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        await mediator.Send(new ResolveMrpExceptionCommand(id, request.ResolutionNotes, userId));
        return NoContent();
    }

    // === Master Schedules ===

    [HttpGet("master-schedules")]
    public async Task<ActionResult<List<MasterScheduleResponseModel>>> GetMasterSchedules(
        [FromQuery] MasterScheduleStatus? status)
    {
        var result = await mediator.Send(new GetMasterSchedulesQuery(status));
        return Ok(result);
    }

    [HttpGet("master-schedules/{id:int}")]
    public async Task<ActionResult<MasterScheduleDetailResponseModel>> GetMasterSchedule(int id)
    {
        var result = await mediator.Send(new GetMasterScheduleDetailQuery(id));
        return Ok(result);
    }

    [HttpPost("master-schedules")]
    public async Task<ActionResult<MasterScheduleDetailResponseModel>> CreateMasterSchedule([FromBody] CreateMasterScheduleRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : 0;
        var result = await mediator.Send(new CreateMasterScheduleCommand(
            request.Name,
            request.Description,
            request.PeriodStart,
            request.PeriodEnd,
            userId,
            request.Lines
        ));
        return CreatedAtAction(nameof(GetMasterSchedule), new { id = result.Id }, result);
    }

    [HttpPut("master-schedules/{id:int}")]
    public async Task<ActionResult<MasterScheduleDetailResponseModel>> UpdateMasterSchedule(int id, [FromBody] UpdateMasterScheduleRequest request)
    {
        var result = await mediator.Send(new UpdateMasterScheduleCommand(
            id,
            request.Name,
            request.Description,
            request.PeriodStart,
            request.PeriodEnd,
            request.Lines
        ));
        return Ok(result);
    }

    [HttpPost("master-schedules/{id:int}/activate")]
    public async Task<ActionResult<MasterScheduleResponseModel>> ActivateMasterSchedule(int id)
    {
        var result = await mediator.Send(new ActivateMasterScheduleCommand(id));
        return Ok(result);
    }

    [HttpGet("master-schedules/{id:int}/vs-actual")]
    public async Task<ActionResult<List<MpsVsActualResponseModel>>> GetMpsVsActual(int id)
    {
        var result = await mediator.Send(new GetMpsVsActualQuery(id));
        return Ok(result);
    }

    // === Demand Forecasts ===

    [HttpGet("forecasts")]
    public async Task<ActionResult<List<DemandForecastResponseModel>>> GetForecasts([FromQuery] int? partId)
    {
        var result = await mediator.Send(new GetDemandForecastsQuery(partId));
        return Ok(result);
    }

    [HttpPost("forecasts")]
    public async Task<ActionResult<DemandForecastResponseModel>> GenerateForecast([FromBody] GenerateForecastRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var result = await mediator.Send(new GenerateDemandForecastCommand(
            request.PartId,
            request.Name,
            request.Method,
            request.HistoricalPeriods,
            request.ForecastPeriods,
            request.SmoothingFactor,
            userId
        ));
        return CreatedAtAction(nameof(GetForecasts), null, result);
    }

    [HttpPost("forecasts/{id:int}/approve")]
    public async Task<IActionResult> ApproveForecast(int id)
    {
        await mediator.Send(new ApproveDemandForecastCommand(id));
        return NoContent();
    }

    [HttpPost("forecasts/{id:int}/apply")]
    public async Task<IActionResult> ApplyForecastToMps(int id, [FromBody] ApplyForecastRequest request)
    {
        await mediator.Send(new ApplyForecastToMpsCommand(id, request.MasterScheduleId));
        return NoContent();
    }

    [HttpPost("forecasts/{forecastId:int}/overrides")]
    public async Task<ActionResult<ForecastOverrideResponseModel>> CreateOverride(int forecastId, [FromBody] CreateOverrideRequest request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        var result = await mediator.Send(new CreateForecastOverrideCommand(
            forecastId,
            request.PeriodStart,
            request.OverrideQuantity,
            request.Reason,
            userId
        ));
        return Created($"api/v1/mrp/forecasts/{forecastId}/overrides/{result.Id}", result);
    }

    // === Part Plan & Pegging ===

    [HttpGet("runs/{runId:int}/parts/{partId:int}/plan")]
    public async Task<ActionResult<MrpPartPlanResponseModel>> GetPartPlan(int runId, int partId)
    {
        var result = await mediator.Send(new GetMrpPartPlanQuery(runId, partId));
        return Ok(result);
    }

    [HttpGet("runs/{runId:int}/parts/{partId:int}/pegging")]
    public async Task<ActionResult<List<MrpPeggingResponseModel>>> GetPegging(int runId, int partId)
    {
        var result = await mediator.Send(new GetMrpPeggingQuery(runId, partId));
        return Ok(result);
    }
}

// Request models
public record ExecuteMrpRunRequest(
    MrpRunType RunType = MrpRunType.Full,
    int PlanningHorizonDays = 90,
    List<int>? PartIds = null
);

public record UpdatePlannedOrderRequest(bool? IsFirmed, string? Notes);
public record BulkReleaseRequest(List<int> Ids);
public record ResolveExceptionRequest(string? ResolutionNotes);

public record CreateMasterScheduleRequest(
    string Name,
    string? Description,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    List<CreateMasterScheduleLineModel> Lines
);

public record GenerateForecastRequest(
    int PartId,
    string Name,
    ForecastMethod Method = ForecastMethod.MovingAverage,
    int HistoricalPeriods = 12,
    int ForecastPeriods = 6,
    double? SmoothingFactor = null
);

public record ApplyForecastRequest(int MasterScheduleId);

public record CreateOverrideRequest(
    DateTimeOffset PeriodStart,
    decimal OverrideQuantity,
    string? Reason
);

public record UpdateMasterScheduleRequest(
    string Name,
    string? Description,
    DateTimeOffset PeriodStart,
    DateTimeOffset PeriodEnd,
    List<UpdateMasterScheduleLineModel> Lines
);
