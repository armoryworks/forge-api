using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Eco;
using Forge.Api.Features.Quality;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/quality")]
[Authorize]
[RequiresCapability("CAP-QC-INSPECTION")]
public class QualityController(IMediator mediator) : ControllerBase
{
    [HttpGet("templates")]
    public async Task<ActionResult<List<QcTemplateResponseModel>>> GetTemplates()
    {
        var result = await mediator.Send(new GetQcTemplatesQuery());
        return Ok(result);
    }

    [HttpPost("templates")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<QcTemplateResponseModel>> CreateTemplate(
        [FromBody] CreateQcTemplateRequestModel request)
    {
        var result = await mediator.Send(new CreateQcTemplateCommand(request));
        return Created($"/api/v1/quality/templates/{result.Id}", result);
    }

    [HttpGet("inspections")]
    public async Task<ActionResult<List<QcInspectionResponseModel>>> GetInspections(
        [FromQuery] int? jobId,
        [FromQuery] string? status,
        [FromQuery] string? lotNumber)
    {
        var result = await mediator.Send(new GetQcInspectionsQuery(jobId, status, lotNumber));
        return Ok(result);
    }

    [HttpPost("inspections")]
    public async Task<ActionResult<QcInspectionResponseModel>> CreateInspection(
        [FromBody] CreateQcInspectionRequestModel request)
    {
        var result = await mediator.Send(new CreateQcInspectionCommand(request));
        return Created($"/api/v1/quality/inspections/{result.Id}", result);
    }

    [HttpPut("inspections/{id:int}")]
    public async Task<ActionResult<QcInspectionResponseModel>> UpdateInspection(
        int id, [FromBody] UpdateQcInspectionRequestModel request)
    {
        var result = await mediator.Send(new UpdateQcInspectionCommand(id, request));
        return Ok(result);
    }

    // ── ECOs ──────────────────────────────────────────────────────────────────

    [HttpGet("ecos")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<ActionResult<List<EcoResponseModel>>> GetEcos([FromQuery] EcoStatus? status)
    {
        var result = await mediator.Send(new GetEcosQuery(status));
        return Ok(result);
    }

    [HttpGet("ecos/{id:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<ActionResult<EcoResponseModel>> GetEcoById(int id)
    {
        var result = await mediator.Send(new GetEcoByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("ecos")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<ActionResult<EcoResponseModel>> CreateEco([FromBody] CreateEcoRequestModel request)
    {
        var result = await mediator.Send(new CreateEcoCommand(request));
        return Created($"/api/v1/quality/ecos/{result.Id}", result);
    }

    [HttpPatch("ecos/{id:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<ActionResult<EcoResponseModel>> UpdateEco(int id, [FromBody] UpdateEcoRequestModel request)
    {
        var result = await mediator.Send(new UpdateEcoCommand(id, request));
        return Ok(result);
    }

    [HttpPost("ecos/{id:int}/approve")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<IActionResult> ApproveEco(int id)
    {
        await mediator.Send(new ApproveEcoCommand(id));
        return NoContent();
    }

    [HttpPost("ecos/{id:int}/implement")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<IActionResult> ImplementEco(int id)
    {
        await mediator.Send(new ImplementEcoCommand(id));
        return NoContent();
    }

    [HttpPost("ecos/{id:int}/affected-items")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<ActionResult<EcoAffectedItemResponseModel>> AddAffectedItem(
        int id, [FromBody] CreateEcoAffectedItemRequestModel request)
    {
        var result = await mediator.Send(new AddEcoAffectedItemCommand(id, request));
        return Created($"/api/v1/quality/ecos/{id}", result);
    }

    [HttpDelete("ecos/{id:int}/affected-items/{itemId:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    [RequiresCapability("CAP-MD-ECO")]
    public async Task<IActionResult> DeleteAffectedItem(int id, int itemId)
    {
        await mediator.Send(new DeleteEcoAffectedItemCommand(id, itemId));
        return NoContent();
    }

    // ── Gages ──

    [HttpGet("gages")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<List<GageResponseModel>>> GetGages(
        [FromQuery] GageStatus? status, [FromQuery] string? search)
    {
        var result = await mediator.Send(new GetGagesQuery(status, search));
        return Ok(result);
    }

    [HttpGet("gages/{id:int}")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<GageResponseModel>> GetGageById(int id)
    {
        var result = await mediator.Send(new GetGageByIdQuery(id));
        return Ok(result);
    }

    [HttpPost("gages")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<GageResponseModel>> CreateGage(
        [FromBody] CreateGageRequestModel request)
    {
        var result = await mediator.Send(new CreateGageCommand(request));
        return Created($"/api/v1/quality/gages/{result.Id}", result);
    }

    [HttpPatch("gages/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<GageResponseModel>> UpdateGage(
        int id, [FromBody] UpdateGageRequestModel request)
    {
        var result = await mediator.Send(new UpdateGageCommand(id, request));
        return Ok(result);
    }

    [HttpGet("gages/due")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<List<GageResponseModel>>> GetGagesDue(
        [FromQuery] int daysAhead = 30)
    {
        var result = await mediator.Send(new GetGagesDueQuery(daysAhead));
        return Ok(result);
    }

    [HttpGet("gages/{id:int}/calibrations")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<List<CalibrationRecordResponseModel>>> GetGageCalibrations(int id)
    {
        var result = await mediator.Send(new GetGageCalibrationsQuery(id));
        return Ok(result);
    }

    [HttpPost("gages/{id:int}/calibrations")]
    [Authorize(Roles = "Admin,Manager")]
    [RequiresCapability("CAP-QC-GAGE")]
    public async Task<ActionResult<CalibrationRecordResponseModel>> CreateCalibration(
        int id, [FromBody] CreateCalibrationRecordRequestModel request)
    {
        var result = await mediator.Send(new CreateCalibrationRecordCommand(id, request));
        return Created($"/api/v1/quality/gages/{id}/calibrations/{result.Id}", result);
    }
}
