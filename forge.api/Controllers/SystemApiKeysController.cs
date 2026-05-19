using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.SystemApiKeys;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Admin endpoint for issuing user-bound system API keys (headless
/// integrations that authenticate AS a real ApplicationUser).
///
/// Gated by:
///   - <c>[Authorize(Roles = "Admin")]</c> — only an admin can mint a key.
///     Critically: the system user that the key authenticates AS does NOT
///     get this endpoint — keys cannot issue keys (privilege-escalation
///     prevention, matching the BI keys policy).
///   - <c>[RequiresCapability("CAP-IDEN-AUTH-API-KEYS")]</c> — capability
///     must be enabled for the install. Default-off for most presets;
///     enabled when the operator opts into headless integrations.
///
/// Companion controller to <see cref="BiApiKeysController"/> — same shape,
/// distinct entity. Lives at <c>/api/v1/admin/system-api-keys</c>.
/// </summary>
[ApiController]
[Route("api/v1/admin/system-api-keys")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-IDEN-AUTH-API-KEYS")]
public class SystemApiKeysController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SystemApiKeyResponseModel>>> GetApiKeys()
    {
        var result = await mediator.Send(new GetSystemApiKeysQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CreateSystemApiKeyResponseModel>> CreateApiKey(
        [FromBody] CreateSystemApiKeyRequestModel model)
    {
        var result = await mediator.Send(new CreateSystemApiKeyCommand(model));
        return Created($"/api/v1/admin/system-api-keys/{result.Id}", result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> RevokeApiKey(int id)
    {
        await mediator.Send(new RevokeSystemApiKeyCommand(id));
        return NoContent();
    }
}
