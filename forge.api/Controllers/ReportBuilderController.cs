using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Reports;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/report-builder")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-RPT-DASHBOARDS")]
public class ReportBuilderController(IMediator mediator) : ControllerBase
{
    [HttpGet("entities")]
    public async Task<ActionResult<List<ReportEntityDefinitionModel>>> GetEntities()
    {
        var result = await mediator.Send(new GetReportEntitiesQuery());
        return Ok(result);
    }

    [HttpGet("saved")]
    public async Task<ActionResult<List<SavedReportResponseModel>>> GetSavedReports()
    {
        var result = await mediator.Send(new GetSavedReportsQuery());
        return Ok(result);
    }

    [HttpGet("saved/{id:int}")]
    public async Task<ActionResult<SavedReportResponseModel>> GetSavedReport(int id)
    {
        var result = await mediator.Send(new GetSavedReportQuery(id));
        return Ok(result);
    }

    [HttpPost("saved")]
    public async Task<ActionResult<SavedReportResponseModel>> CreateSavedReport([FromBody] CreateSavedReportRequestModel request)
    {
        var result = await mediator.Send(new CreateSavedReportCommand(
            request.Name,
            request.Description,
            request.EntitySource,
            request.Columns,
            request.Filters,
            request.GroupByField,
            request.SortField,
            request.SortDirection,
            request.ChartType,
            request.ChartLabelField,
            request.ChartValueField,
            request.IsShared));

        return CreatedAtAction(nameof(GetSavedReport), new { id = result.Id }, result);
    }

    [HttpPut("saved/{id:int}")]
    public async Task<ActionResult<SavedReportResponseModel>> UpdateSavedReport(int id, [FromBody] UpdateSavedReportRequestModel request)
    {
        var result = await mediator.Send(new UpdateSavedReportCommand(
            id,
            request.Name,
            request.Description,
            request.EntitySource,
            request.Columns,
            request.Filters,
            request.GroupByField,
            request.SortField,
            request.SortDirection,
            request.ChartType,
            request.ChartLabelField,
            request.ChartValueField,
            request.IsShared));

        return Ok(result);
    }

    [HttpDelete("saved/{id:int}")]
    public async Task<IActionResult> DeleteSavedReport(int id)
    {
        await mediator.Send(new DeleteSavedReportCommand(id));
        return NoContent();
    }

    [HttpPost("run")]
    public async Task<ActionResult<RunReportResponseModel>> RunReport([FromBody] RunReportRequestModel request)
    {
        var result = await mediator.Send(new RunReportCommand(
            request.EntitySource,
            request.Columns,
            request.Filters,
            request.GroupByField,
            request.SortField,
            request.SortDirection,
            request.Page,
            request.PageSize));

        return Ok(result);
    }

    [HttpGet("{id:int}/export")]
    public async Task<IActionResult> ExportReport(int id, [FromQuery] ReportExportFormat format = ReportExportFormat.Csv)
    {
        var result = await mediator.Send(new ExportReportQuery(id, format));
        return File(result.Content, result.ContentType, result.FileName);
    }

    // --- Report Schedules ---

    [HttpGet("schedules")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<ReportScheduleResponseModel>>> GetSchedules()
    {
        var result = await mediator.Send(new GetReportSchedulesQuery());
        return Ok(result);
    }

    [HttpPost("schedules")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<ReportScheduleResponseModel>> CreateSchedule([FromBody] CreateReportScheduleRequestModel request)
    {
        var result = await mediator.Send(new CreateReportScheduleCommand(
            request.SavedReportId,
            request.CronExpression,
            request.RecipientEmailsJson,
            request.Format,
            request.SubjectTemplate));

        return CreatedAtAction(nameof(GetSchedules), new { }, result);
    }

    [HttpDelete("schedules/{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteSchedule(int id)
    {
        await mediator.Send(new DeleteReportScheduleCommand(id));
        return NoContent();
    }
}
