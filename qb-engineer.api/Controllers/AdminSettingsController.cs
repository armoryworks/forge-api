using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using QBEngineer.Api.Capabilities;
using QBEngineer.Api.Features.Settings;
using QBEngineer.Core.Settings;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Phase 1m — admin-managed settings surface. Admin-only. Bootstrap-
/// exempt because the descriptor catalog itself describes the settings
/// the install needs to be brought up — gating the catalog behind a
/// capability you can only enable through this surface would deadlock.
/// </summary>
[ApiController]
[Route("api/v1/admin/settings")]
[Authorize(Roles = "Admin")]
[CapabilityBootstrap]
public class AdminSettingsController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Lists all setting groups present in the descriptor catalog. The
    /// admin UI uses this to render the left-rail nav.
    /// </summary>
    [HttpGet("groups")]
    public ActionResult<IReadOnlyList<string>> GetGroups()
        => Ok(SettingDescriptorCatalog.Groups);

    /// <summary>
    /// Lists settings within a group (or all groups when no filter).
    /// Secrets are masked in the response.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<List<SettingsCatalogEntry>>> GetCatalog(
        [FromQuery] string? group, CancellationToken ct)
        => Ok(await mediator.Send(new GetSettingsCatalogQuery(group), ct));

    /// <summary>
    /// Update a single setting. Empty body / null value erases the
    /// stored row → next read returns the descriptor's DefaultValue.
    /// </summary>
    [HttpPut("{key}")]
    public async Task<IActionResult> UpdateSetting(string key, [FromBody] UpdateSettingBody body, CancellationToken ct)
    {
        await mediator.Send(new UpdateSettingCommand(key, body.Value), ct);
        return NoContent();
    }

    public sealed record UpdateSettingBody(string? Value);
}
