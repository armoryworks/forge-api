using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Maintenance;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/predictions")]
[Authorize]
[RequiresCapability("CAP-MAINT-PREDICTIVE")]
public class PredictiveMaintenanceController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<MaintenancePredictionResponseModel>>> GetPredictions(
        [FromQuery] int? workCenterId,
        [FromQuery] MaintenancePredictionSeverity? severity,
        [FromQuery] MaintenancePredictionStatus? status)
    {
        var result = await mediator.Send(new GetPredictionsQuery(workCenterId, severity, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<MaintenancePredictionResponseModel>> GetPrediction(int id)
    {
        var result = await mediator.Send(new GetPredictionQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id)
    {
        await mediator.Send(new AcknowledgePredictionCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/schedule-maintenance")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> ScheduleMaintenance(int id)
    {
        await mediator.Send(new SchedulePreventiveMaintenanceCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> Resolve(int id, [FromBody] ResolvePredictionRequestModel request)
    {
        await mediator.Send(new ResolvePredictionCommand(id, request));
        return NoContent();
    }

    [HttpPost("{id:int}/false-positive")]
    public async Task<IActionResult> MarkFalsePositive(int id, [FromBody] ResolvePredictionRequestModel request)
    {
        await mediator.Send(new MarkFalsePositiveCommand(id, request));
        return NoContent();
    }

    [HttpPost("{id:int}/feedback")]
    public async Task<IActionResult> RecordFeedback(int id, [FromBody] RecordPredictionFeedbackRequestModel request)
    {
        await mediator.Send(new RecordPredictionFeedbackCommand(id, request));
        return NoContent();
    }

    [HttpGet("dashboard")]
    public async Task<ActionResult<PredictiveMaintenanceDashboardResponseModel>> GetDashboard()
    {
        var result = await mediator.Send(new GetPredictiveMaintenanceDashboardQuery());
        return Ok(result);
    }

    [HttpPost("run/{workCenterId:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> TriggerRun(int workCenterId)
    {
        await mediator.Send(new TriggerPredictionRunCommand(workCenterId));
        return Accepted();
    }

    [HttpGet("~/api/v1/ml-models")]
    public async Task<ActionResult<List<MlModelResponseModel>>> GetModels()
    {
        var result = await mediator.Send(new GetMlModelsQuery());
        return Ok(result);
    }

    [HttpGet("~/api/v1/ml-models/{modelId}/performance")]
    public async Task<ActionResult<MlModelPerformanceResponseModel>> GetModelPerformance(string modelId)
    {
        var result = await mediator.Send(new GetModelPerformanceQuery(modelId));
        return Ok(result);
    }
}
