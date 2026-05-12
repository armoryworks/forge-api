using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/spc")]
[Authorize]
[RequiresCapability("CAP-QC-SPC")]
public class SpcController(IMediator mediator) : ControllerBase
{
    [HttpGet("characteristics")]
    public async Task<ActionResult<List<SpcCharacteristicResponseModel>>> GetCharacteristics(
        [FromQuery] int? partId,
        [FromQuery] int? operationId,
        [FromQuery] bool? isActive)
    {
        var result = await mediator.Send(new GetSpcCharacteristicsQuery(partId, operationId, isActive));
        return Ok(result);
    }

    [HttpPost("characteristics")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SpcCharacteristicResponseModel>> CreateCharacteristic(
        [FromBody] CreateSpcCharacteristicRequestModel request)
    {
        var result = await mediator.Send(new CreateSpcCharacteristicCommand(request));
        return Created($"/api/v1/spc/characteristics/{result.Id}", result);
    }

    [HttpGet("characteristics/{id:int}")]
    public async Task<ActionResult<SpcCharacteristicResponseModel>> GetCharacteristic(int id)
    {
        var result = await mediator.Send(new GetSpcCharacteristicsQuery(null, null, null));
        var characteristic = result.FirstOrDefault(c => c.Id == id);
        if (characteristic == null)
            return NotFound();
        return Ok(characteristic);
    }

    [HttpPut("characteristics/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SpcCharacteristicResponseModel>> UpdateCharacteristic(
        int id, [FromBody] UpdateSpcCharacteristicRequestModel request)
    {
        var result = await mediator.Send(new UpdateSpcCharacteristicCommand(id, request));
        return Ok(result);
    }

    [HttpGet("characteristics/{id:int}/chart")]
    public async Task<ActionResult<SpcChartDataModel>> GetChartData(
        int id, [FromQuery] int? lastN)
    {
        var result = await mediator.Send(new GetSpcChartDataQuery(id, lastN));
        return Ok(result);
    }

    [HttpPost("measurements")]
    public async Task<ActionResult<List<SpcMeasurementResponseModel>>> RecordMeasurements(
        [FromBody] RecordSpcMeasurementRequestModel request)
    {
        var result = await mediator.Send(new RecordSpcMeasurementsCommand(request));
        return Created("/api/v1/spc/measurements", result);
    }

    [HttpGet("measurements")]
    public async Task<ActionResult<List<SpcMeasurementResponseModel>>> GetMeasurements(
        [FromQuery] int? characteristicId,
        [FromQuery] DateTimeOffset? dateFrom,
        [FromQuery] DateTimeOffset? dateTo,
        [FromQuery] int? jobId)
    {
        var result = await mediator.Send(new GetSpcMeasurementsQuery(characteristicId, dateFrom, dateTo, jobId));
        return Ok(result);
    }

    [HttpPost("characteristics/{id:int}/recalculate-limits")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SpcControlLimitModel>> RecalculateLimits(
        int id, [FromQuery] int? fromSubgroup, [FromQuery] int? toSubgroup)
    {
        var result = await mediator.Send(new RecalculateControlLimitsCommand(id, fromSubgroup, toSubgroup));
        return Ok(result);
    }

    [HttpGet("capability/{characteristicId:int}")]
    public async Task<ActionResult<SpcCapabilityReportModel>> GetProcessCapability(int characteristicId)
    {
        var result = await mediator.Send(new GetProcessCapabilityQuery(characteristicId));
        return Ok(result);
    }

    [HttpGet("out-of-control")]
    public async Task<ActionResult<List<SpcOocEventResponseModel>>> GetOocEvents(
        [FromQuery] SpcOocStatus? status,
        [FromQuery] SpcOocSeverity? severity,
        [FromQuery] int? characteristicId)
    {
        var result = await mediator.Send(new GetOocEventsQuery(status, severity, characteristicId));
        return Ok(result);
    }

    [HttpPost("out-of-control/{id:int}/acknowledge")]
    public async Task<ActionResult<SpcOocEventResponseModel>> AcknowledgeOocEvent(
        int id, [FromBody] AcknowledgeOocRequestModel request)
    {
        var result = await mediator.Send(new AcknowledgeOocEventCommand(id, request));
        return Ok(result);
    }

    [HttpPost("out-of-control/{id:int}/create-capa")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SpcOocEventResponseModel>> CreateCapaFromOoc(int id)
    {
        var result = await mediator.Send(new CreateCapaFromOocCommand(id));
        return Ok(result);
    }
}
