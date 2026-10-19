using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Reports;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/reports")]
[Authorize]
[RequiresCapability("CAP-RPT-OPERATIONAL")]
public class MyReportsController(IMediator mediator) : ControllerBase
{
    [HttpGet("my-work-history")]
    public async Task<ActionResult<List<MyWorkHistoryReportItem>>> GetMyWorkHistory()
    {
        var result = await mediator.Send(new GetMyWorkHistoryQuery());
        return Ok(result);
    }

    [HttpGet("my-time-log")]
    public async Task<ActionResult<List<MyTimeLogReportItem>>> GetMyTimeLog(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetMyTimeLogQuery(start, end));
        return Ok(result);
    }

    [HttpGet("my-expense-history")]
    public async Task<ActionResult<List<MyExpenseHistoryReportItem>>> GetMyExpenseHistory(
        [FromQuery] DateTimeOffset start, [FromQuery] DateTimeOffset end)
    {
        var result = await mediator.Send(new GetMyExpenseHistoryQuery(start, end));
        return Ok(result);
    }

    [HttpGet("my-cycle-summary")]
    public async Task<ActionResult<List<MyCycleSummaryReportItem>>> GetMyCycleSummary()
    {
        var result = await mediator.Send(new GetMyCycleSummaryReportQuery());
        return Ok(result);
    }
}
