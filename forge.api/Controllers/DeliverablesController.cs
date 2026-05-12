using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Deliverables;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Pro Services rollout (Artifact 4 §4.6) — CRUD over <c>Deliverable</c>
/// entities. Gated by <c>CAP-O2C-DELIVERABLE</c>.
///
/// <para>Roles: Admin / Manager / PM / Engineer cover the typical Pro
/// Services personae (Engagement Manager, Practitioner, Delivery Lead).</para>
/// </summary>
[ApiController]
[Route("api/v1/deliverables")]
[Authorize(Roles = "Admin,Manager,PM,Engineer")]
[RequiresCapability("CAP-O2C-DELIVERABLE")]
public class DeliverablesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DeliverableListResponseModel>> List(
        [FromQuery] int? jobId,
        [FromQuery] int? projectId,
        [FromQuery] int? customerId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new GetDeliverablesQuery(jobId, projectId, customerId, status, page, pageSize),
            ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<DeliverableResponseModel>> Get(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetDeliverableQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<DeliverableResponseModel>> Create(
        [FromBody] CreateDeliverableRequestModel request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new CreateDeliverableCommand(request), ct);
        return Created($"/api/v1/deliverables/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<DeliverableResponseModel>> Update(
        int id,
        [FromBody] UpdateDeliverableRequestModel request,
        CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateDeliverableCommand(id, request), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager,PM")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteDeliverableCommand(id), ct);
        return NoContent();
    }
}
