using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Settings;

namespace Forge.Api.Controllers;

/// <summary>
/// Client-facing configuration for the editable business-identifier feature. Bootstrap-exempt
/// (like the descriptor / reference-data endpoints) so every create/edit-capable role can read the
/// per-entity manual-number flags without the Admin-only system-settings endpoint.
/// </summary>
[ApiController]
[Route("api/v1/identifier-settings")]
[Authorize]
[CapabilityBootstrap]
public class IdentifierSettingsController(IMediator mediator) : ControllerBase
{
    /// <summary>Per-entity "allow manual numbers" flags.</summary>
    [HttpGet("manual-numbers")]
    public async Task<ActionResult<ManualNumberSettingsResponseModel>> GetManualNumberSettings(CancellationToken ct)
    {
        var result = await mediator.Send(new GetManualNumberSettingsQuery(), ct);
        return Ok(result);
    }
}
