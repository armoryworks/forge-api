using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Admin.Telemetry;
using Forge.Api.Features.Settings;
using Forge.Core.Models;
using Forge.Core.Settings;

namespace Forge.Api.Controllers;

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
    // ── Remote health monitoring (opt-in) ───────────────────────────────────────
    // Lives on the settings surface rather than behind a capability: this is a
    // consent decision the business owner makes about their own data leaving the
    // building, not a feature an integrator switches on. It belongs next to the
    // company profile, not in a capability matrix.

    /// <summary>The agreement to show before opting in, with the verbatim sample payload.</summary>
    [HttpGet("telemetry/agreement")]
    public async Task<ActionResult<TelemetryAgreementResponseModel>> GetTelemetryAgreement(CancellationToken ct)
        => Ok(await mediator.Send(new GetTelemetryAgreementQuery(), ct));

    /// <summary>Whether monitoring is on, how the enrollment stands, and who last decided.</summary>
    [HttpGet("telemetry")]
    public async Task<ActionResult<TelemetryStatusResponseModel>> GetTelemetryStatus(CancellationToken ct)
        => Ok(await mediator.Send(new GetTelemetryStatusQuery(), ct));

    /// <summary>
    /// Record the operator's answer. Both answers are kept — being able to show that
    /// monitoring was offered and declined is as much a part of the record as a yes.
    /// </summary>
    [HttpPost("telemetry/consent")]
    public async Task<ActionResult<TelemetryStatusResponseModel>> RecordTelemetryConsent(
        [FromBody] TelemetryConsentRequest body, CancellationToken ct)
        => Ok(await mediator.Send(new RecordTelemetryConsentCommand(
            body.Accepted,
            body.AcceptedByEmail,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            Request.Headers.UserAgent.ToString()), ct));

    /// <summary>Every consent decision this install has recorded, newest first.</summary>
    [HttpGet("telemetry/consent-history")]
    public async Task<ActionResult<IReadOnlyList<TelemetryConsentRecordResponseModel>>> GetTelemetryConsentHistory(
        [FromQuery] int take = 25, CancellationToken ct = default)
        => Ok(await mediator.Send(new GetTelemetryConsentHistoryQuery(take), ct));

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

/// <summary>
/// Body for the consent endpoint. The agreement version is deliberately NOT taken
/// from the client — the server stamps what it currently ships, so a stale tab can't
/// record agreement to superseded terms.
/// </summary>
public sealed record TelemetryConsentRequest(bool Accepted, string? AcceptedByEmail);
