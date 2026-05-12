using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Entities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/domain-event-failures")]
[Authorize(Roles = "Admin")]
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
