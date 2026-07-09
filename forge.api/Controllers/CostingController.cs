using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Costing;

namespace Forge.Api.Controllers;

/// <summary>
/// Tier 2 costing configuration — read/write the active <c>CostingProfile</c> (mode + departmental
/// per-work-center overhead rates). Whole surface is gated by CAP-COSTING-TIER2-DEPTRATES; the rollup
/// engine falls back to flat Tier-1 rates whenever the capability is off or the mode is not departmental.
/// </summary>
[ApiController]
[Route("api/v1/costing")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-COSTING-TIER2-DEPTRATES")]
public class CostingController(IMediator mediator) : ControllerBase
{
    [HttpGet("profile")]
    public async Task<ActionResult<CostingProfileResponseModel>> GetProfile()
    {
        var result = await mediator.Send(new GetCostingProfileQuery());
        return Ok(result);
    }

    [HttpPut("profile")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateCostingProfileCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }
}
