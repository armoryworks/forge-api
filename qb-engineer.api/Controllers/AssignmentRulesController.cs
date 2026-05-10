using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using QBEngineer.Api.Capabilities;
using QBEngineer.Api.Features.AssignmentRules;
using QBEngineer.Core.Models;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Phase 1r / Batch 11 — admin CRUD over lead-assignment rules. Same
/// CAP-O2C-LEAD gating + Admin/Manager only for mutations.
/// </summary>
[ApiController]
[Route("api/v1/assignment-rules")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-O2C-LEAD")]
public class AssignmentRulesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<AssignmentRuleResponseModel>>> Get([FromQuery] bool? activeOnly)
        => Ok(await mediator.Send(new GetAssignmentRulesQuery(activeOnly)));

    [HttpPost]
    public async Task<ActionResult<AssignmentRuleResponseModel>> Create([FromBody] CreateAssignmentRuleRequest request)
    {
        var result = await mediator.Send(new CreateAssignmentRuleCommand(request));
        return Created($"/api/v1/assignment-rules/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<AssignmentRuleResponseModel>> Update(int id, [FromBody] UpdateAssignmentRuleRequest request)
        => Ok(await mediator.Send(new UpdateAssignmentRuleCommand(id, request)));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteAssignmentRuleCommand(id));
        return NoContent();
    }
}
