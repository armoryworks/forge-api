using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using Forge.Api.Capabilities;
using Forge.Api.Features.Capabilities.AuditLog;
using Forge.Api.Features.Capabilities.BulkToggle;
using Forge.Api.Features.Capabilities.Config;
using Forge.Api.Features.Capabilities.Descriptor;
using Forge.Api.Features.Capabilities.Relations;
using Forge.Api.Features.Capabilities.Toggle;
using Forge.Api.Features.Capabilities.Validate;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase 4 Phase-A — Read-only capability descriptor surface.
/// Phase 4 Phase-B — Adds the minimum viable toggle mutation.
/// Phase 4 Phase-C — Extends the mutation surface with optimistic concurrency
/// (If-Match / 412), dependency / mutex enforcement (409 with envelope), the
/// config endpoint, and the bulk-toggle endpoint. All three mutation routes
/// emit richer audit content (before/after, actor, optional reason).
///
/// Per Phase B D3/D4 (and Phase C reaffirmation): admin endpoints are gated
/// by the <c>Admin</c> role and carry <c>[CapabilityBootstrap]</c> so they
/// are NOT themselves capability-gated — disabling
/// <c>CAP-IDEN-CAPABILITY-ADMIN</c> would otherwise brick recovery.
/// </summary>
[ApiController]
[Route("api/v1/capabilities")]
[Authorize]
public class CapabilitiesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 4 Phase-A — full capability descriptor for the installation.
    /// Phase B's UI calls this on login and on SignalR <c>capabilityChanged</c>.
    /// Phase C — each entry carries an <c>etag</c> the admin UI hands back as
    /// <c>If-Match</c> on subsequent toggle / config writes.
    /// </summary>
    [HttpGet("descriptor")]
    [CapabilityBootstrap]
    public async Task<ActionResult<CapabilityDescriptorResponseModel>> GetDescriptor()
    {
        var result = await mediator.Send(new GetCapabilityDescriptorQuery());
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-B / Phase-C — toggles a single capability's enabled flag.
    ///
    /// Phase C semantics:
    ///   • <c>If-Match</c> header (weak ETag of the row's <c>Version</c>):
    ///     mismatch → 412 with WU-02 envelope.
    ///   • Disable when other enabled capabilities depend on this row →
    ///     409 with <c>capability-has-dependents</c>.
    ///   • Enable when required dependencies are disabled →
    ///     409 with <c>capability-missing-dependencies</c>.
    ///   • Enable when a soft-mutex peer is enabled →
    ///     409 with <c>capability-mutex-violation</c>.
    /// </summary>
    [HttpPut("{id}/enabled")]
    [Authorize(Roles = "Admin")]
    [CapabilityBootstrap]
    public async Task<ActionResult<CapabilityDescriptorEntry>> SetEnabled(
        string id,
        [FromBody] ToggleCapabilityRequestModel body,
        CancellationToken ct)
    {
        var ifMatch = Request.Headers[HeaderNames.IfMatch].ToString();
        try
        {
            var result = await mediator.Send(
                new ToggleCapabilityCommand(id, body.Enabled, ifMatch, body.Reason),
                ct);
            Response.Headers[HeaderNames.ETag] = result.ETag;
            return Ok(result);
        }
        catch (CapabilityMutationException ex)
        {
            return ToCapabilityMutationResult(ex);
        }
    }

    /// <summary>
    /// Phase 4 Phase-C — updates a capability's opaque config payload.
    /// Optimistic concurrency via <c>If-Match</c> against the
    /// CapabilityConfig row's separate Version (toggles and config edits
    /// each have their own ETag space).
    /// </summary>
    [HttpPut("{id}/config")]
    [Authorize(Roles = "Admin")]
    [CapabilityBootstrap]
    public async Task<ActionResult<CapabilityDescriptorEntry>> SetConfig(
        string id,
        [FromBody] UpdateCapabilityConfigRequestModel body,
        CancellationToken ct)
    {
        var ifMatch = Request.Headers[HeaderNames.IfMatch].ToString();
        try
        {
            var result = await mediator.Send(
                new UpdateCapabilityConfigCommand(id, body.ConfigJson, ifMatch, body.Reason),
                ct);
            if (!string.IsNullOrEmpty(result.ConfigETag))
                Response.Headers[HeaderNames.ETag] = result.ConfigETag;
            return Ok(result);
        }
        catch (CapabilityMutationException ex)
        {
            return ToCapabilityMutationResult(ex);
        }
    }

    /// <summary>
    /// Phase 4 Phase-C — atomic bulk toggle. Validates ALL dependency / mutex
    /// constraints across the WHOLE candidate state set before applying any
    /// change. Used as the substrate for Phase G's preset-apply.
    /// </summary>
    [HttpPost("bulk-toggle")]
    [Authorize(Roles = "Admin")]
    [CapabilityBootstrap]
    public async Task<ActionResult<IReadOnlyList<CapabilityDescriptorEntry>>> BulkToggle(
        [FromBody] BulkToggleCapabilitiesRequestModel body,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new BulkToggleCapabilitiesCommand(body.Items, body.Reason),
                ct);
            return Ok(result);
        }
        catch (CapabilityMutationException ex)
        {
            return ToCapabilityMutationResult(ex);
        }
    }

    /// <summary>
    /// Phase 4 Phase-E — Returns audit-log entries scoped to a single
    /// capability. Drives the per-capability detail page's "Recent activity"
    /// section (4E §Screen 5; 4E-decisions-log #8). Pagination via cursor:
    /// <c>?before=&lt;timestamp&gt;&amp;take=N</c>. Admin-only.
    /// </summary>
    [HttpGet("{id}/audit-log")]
    [Authorize(Roles = "Admin")]
    [CapabilityBootstrap]
    public async Task<ActionResult<IReadOnlyList<AuditLogEntryResponseModel>>> GetAuditLog(
        string id,
        [FromQuery] DateTimeOffset? before,
        [FromQuery] int take = 25,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCapabilityAuditLogQuery(id, before, take), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-E — Returns the dependency graph for a single capability:
    /// what it depends on (Dependencies), what depends on it (Dependents), and
    /// what is mutually exclusive with it (Mutexes). Each entry is augmented
    /// with the peer's current name, area, and enabled state so the UI doesn't
    /// have to walk the full descriptor to compute the inverse graph.
    /// </summary>
    [HttpGet("{id}/relations")]
    [Authorize(Roles = "Admin")] // F-13-CAP-04: capability topology is admin-only (matches sibling admin endpoints)
    [CapabilityBootstrap]
    public async Task<ActionResult<CapabilityRelationsResponseModel>> GetRelations(
        string id,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCapabilityRelationsQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-E — Validate-only ("dry run") variant of bulk-toggle.
    /// Returns the same constraint-violation envelope the bulk-toggle would,
    /// but does NOT persist anything. Useful for client-side preview before
    /// committing a multi-toggle change (consumed by the Phase G preset-apply
    /// confirmation modal in a later phase). Admin-only.
    /// </summary>
    [HttpPost("validate")]
    [Authorize(Roles = "Admin")]
    [CapabilityBootstrap]
    public async Task<ActionResult<ValidateCapabilityChangesResponseModel>> Validate(
        [FromBody] ValidateCapabilityChangesRequestModel body,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(
            new ValidateCapabilityChangesCommand(body.Items),
            ct);
        return Ok(result);
    }

    private ActionResult ToCapabilityMutationResult(CapabilityMutationException ex)
    {
        Response.ContentType = "application/problem+json";
        return new ObjectResult(ex.ToEnvelope())
        {
            StatusCode = ex.StatusCode,
        };
    }
}

/// <summary>
/// Phase 4 Phase-E — Body for <c>POST /api/v1/capabilities/validate</c>.
/// Mirrors <see cref="Forge.Api.Features.Capabilities.Validate.ValidateChangeItem"/>
/// at the request shape so the controller doesn't bind directly to the
/// MediatR command record (which would also require duplicate-id validation
/// at bind time).
/// </summary>
public record ValidateCapabilityChangesRequestModel(IReadOnlyList<ValidateChangeItem> Items);
