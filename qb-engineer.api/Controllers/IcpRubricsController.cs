using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using QBEngineer.Api.Capabilities;
using QBEngineer.Api.Features.IcpRubrics;
using QBEngineer.Core.Models;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Phase 1r / Batch 10 — admin CRUD over ICP rubrics + their dimensions.
/// Same CAP-O2C-LEAD gating as the rest of the lead-intake surfaces.
///
/// Dimension save is a bulk endpoint (POST /{id}/dimensions) — admins
/// edit several at a time in the rubric editor and we want one round-
/// trip + one activity row instead of N.
/// </summary>
[ApiController]
[Route("api/v1/icp-rubrics")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-O2C-LEAD")]
public class IcpRubricsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<IcpRubricResponseModel>>> Get([FromQuery] bool? activeOnly)
        => Ok(await mediator.Send(new GetIcpRubricsQuery(activeOnly)));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<IcpRubricDetailResponseModel>> GetById(int id)
        => Ok(await mediator.Send(new GetIcpRubricByIdQuery(id)));

    [HttpPost]
    public async Task<ActionResult<IcpRubricResponseModel>> Create([FromBody] CreateIcpRubricRequest request)
    {
        var result = await mediator.Send(new CreateIcpRubricCommand(request));
        return Created($"/api/v1/icp-rubrics/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<IcpRubricResponseModel>> Update(int id, [FromBody] UpdateIcpRubricRequest request)
        => Ok(await mediator.Send(new UpdateIcpRubricCommand(id, request)));

    [HttpPost("{id:int}/dimensions")]
    public async Task<ActionResult<IcpRubricDetailResponseModel>> SaveDimensions(
        int id, [FromBody] List<SaveIcpDimensionRequest> dimensions)
        => Ok(await mediator.Send(new SaveIcpDimensionsCommand(id, dimensions)));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteIcpRubricCommand(id));
        return NoContent();
    }
}
