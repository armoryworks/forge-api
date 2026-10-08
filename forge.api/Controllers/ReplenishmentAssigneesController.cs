using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Replenishment;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/replenishment/assignee-candidates")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-PLAN-SAFETYSTOCK")]
public class ReplenishmentAssigneesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ReplenishmentAssigneeCandidateResponseModel>>> GetCandidates(CancellationToken ct)
    {
        var result = await mediator.Send(new GetReplenishmentAssigneeCandidatesQuery(), ct);
        return Ok(result);
    }
}
