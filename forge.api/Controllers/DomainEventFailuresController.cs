using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/domain-event-failures")]
[Authorize(Roles = "Admin")]
// admin recovery surface for failed domain events — must stay reachable when an install is in a bad state
[CapabilityBootstrap]
public class DomainEventFailuresController(DomainEventFailureService failureService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<DomainEventFailure>>> GetFailures(CancellationToken ct)
    {
        var failures = await failureService.GetAll(ct);
        return Ok(failures);
    }

    [HttpPost("{id:int}/retry")]
    public async Task<IActionResult> Retry(int id, CancellationToken ct)
    {
        await failureService.MarkRetrying(id, ct);
        return NoContent();
    }

    [HttpPost("{id:int}/resolve")]
    public async Task<IActionResult> Resolve(int id, CancellationToken ct)
    {
        await failureService.MarkResolved(id, ct);
        return NoContent();
    }
}
