using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.I18nLabelOverrides;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/i18n/overrides")]
[Authorize]
[RequiresCapability("CAP-ADMIN-I18N")]
public class I18nLabelOverridesController(IMediator mediator) : ControllerBase
{
    /// <summary>Merge map for the client i18n loader: language code → (key → override value).</summary>
    [HttpGet("active")]
    public async Task<ActionResult<Dictionary<string, Dictionary<string, string>>>> GetActive(CancellationToken ct)
        => Ok(await mediator.Send(new GetActiveI18nLabelOverridesQuery(), ct));

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<I18nLabelOverrideResponseModel>>> GetAll(CancellationToken ct)
        => Ok(await mediator.Send(new GetI18nLabelOverridesQuery(), ct));

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<UpsertI18nLabelOverrideResponseModel>> Upsert(
        [FromBody] UpsertI18nLabelOverrideRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpsertI18nLabelOverrideCommand(request), ct));

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Revert(int id, CancellationToken ct)
    {
        await mediator.Send(new RevertI18nLabelOverrideCommand(id), ct);
        return NoContent();
    }

    [HttpPost("retry-pending")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<RetryPendingI18nTranslationsResponseModel>> RetryPending(CancellationToken ct)
        => Ok(await mediator.Send(new RetryPendingI18nTranslationsCommand(), ct));
}
