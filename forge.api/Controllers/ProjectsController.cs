using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Projects;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/projects")]
[Authorize(Roles = "Admin,Manager,PM,Engineer")]
[RequiresCapability("CAP-EXT-PROJECTS")]
public class ProjectsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetProjects(
        [FromQuery] string? status,
        [FromQuery] int? customerId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await mediator.Send(new GetProjectsQuery(status, customerId, page, pageSize));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProjectResponseModel>> GetProject(int id)
    {
        var result = await mediator.Send(new GetProjectQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ProjectResponseModel>> CreateProject([FromBody] CreateProjectRequestModel request)
    {
        var result = await mediator.Send(new CreateProjectCommand(request));
        return Created($"/api/v1/projects/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProjectResponseModel>> UpdateProject(int id, [FromBody] UpdateProjectRequestModel request)
    {
        var result = await mediator.Send(new UpdateProjectCommand(id, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteProject(int id)
    {
        await mediator.Send(new DeleteProjectCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/wbs")]
    public async Task<ActionResult> AddWbsElement(int id, [FromBody] CreateWbsElementRequestModel request)
    {
        var elementId = await mediator.Send(new AddWbsElementCommand(id, request));
        return Created($"/api/v1/projects/{id}/wbs/{elementId}", new { id = elementId });
    }

    [HttpPut("{id:int}/wbs/{elementId:int}")]
    public async Task<IActionResult> UpdateWbsElement(int id, int elementId, [FromBody] UpdateWbsElementRequestModel request)
    {
        await mediator.Send(new UpdateWbsElementCommand(id, elementId, request));
        return NoContent();
    }

    [HttpDelete("{id:int}/wbs/{elementId:int}")]
    public async Task<IActionResult> DeleteWbsElement(int id, int elementId)
    {
        await mediator.Send(new DeleteWbsElementCommand(id, elementId));
        return NoContent();
    }

    [HttpPost("{id:int}/wbs/{elementId:int}/costs")]
    public async Task<IActionResult> AddWbsCostEntry(int id, int elementId, [FromBody] CreateWbsCostEntryRequestModel request)
    {
        await mediator.Send(new AddWbsCostEntryCommand(id, elementId, request));
        return NoContent();
    }

    [HttpGet("{id:int}/summary")]
    public async Task<ActionResult<ProjectSummaryResponseModel>> GetProjectSummary(int id)
    {
        var result = await mediator.Send(new GetProjectSummaryQuery(id));
        return Ok(result);
    }

    [HttpGet("{id:int}/earned-value")]
    public async Task<ActionResult<EarnedValueMetricsResponseModel>> GetEarnedValueMetrics(int id)
    {
        var result = await mediator.Send(new GetEarnedValueMetricsQuery(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/recalculate")]
    public async Task<IActionResult> RecalculateProjectTotals(int id)
    {
        await mediator.Send(new RecalculateProjectTotalsCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/wbs/{elementId:int}/link-job")]
    public async Task<IActionResult> LinkJobToWbs(int id, int elementId, [FromBody] LinkJobToWbsRequestModel request)
    {
        await mediator.Send(new LinkJobToWbsCommand(id, elementId, request));
        return NoContent();
    }
}
