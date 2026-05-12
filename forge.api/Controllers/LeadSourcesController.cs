using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.LeadSources;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase 1r / Batch 9 — admin CRUD over the LeadSource catalog. Same
/// CAP-O2C-LEAD gating as the leads + outreach-campaigns controllers,
/// since lead sources are inseparable from lead intake.
///
/// Code is set at create time and never changed — downstream imports +
/// referrer URLs depend on it. Name + Description + IsActive are the
/// admin-editable fields.
/// </summary>
[ApiController]
[Route("api/v1/lead-sources")]
[Authorize(Roles = "Admin,Manager,PM")]
[RequiresCapability("CAP-O2C-LEAD")]
public class LeadSourcesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<LeadSourceResponseModel>>> Get([FromQuery] bool? activeOnly)
        => Ok(await mediator.Send(new GetLeadSourcesQuery(activeOnly)));

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<LeadSourceResponseModel>> Create([FromBody] CreateLeadSourceRequest request)
    {
        var result = await mediator.Send(new CreateLeadSourceCommand(request));
        return Created($"/api/v1/lead-sources/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<LeadSourceResponseModel>> Update(int id, [FromBody] UpdateLeadSourceRequest request)
        => Ok(await mediator.Send(new UpdateLeadSourceCommand(id, request)));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteLeadSourceCommand(id));
        return NoContent();
    }
}
