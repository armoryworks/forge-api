using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Presets.Apply;
using Forge.Api.Features.Presets.Compare;
using Forge.Api.Features.Presets.Custom;
using Forge.Api.Features.Presets.Detail;
using Forge.Api.Features.Presets.List;
using Forge.Api.Features.Presets.Models;
using Forge.Api.Features.Presets.Preview;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase 4 Phase-G — Preset browser + apply orchestration. Standalone
/// surface that lets admins browse / compare / customize / apply presets
/// without going through the discovery wizard. Ships alongside Phase F
/// discovery — the two surfaces share the underlying capability mutation
/// substrate (bulk-toggle handler).
///
/// Endpoints:
///   • GET /api/v1/presets                 — list (8 presets)
///   • GET /api/v1/presets/{id}            — single preset detail with deltas
///   • POST /api/v1/presets/compare        — side-by-side matrix (2-4 presets)
///   • POST /api/v1/presets/{id}/preview-apply — deltas + violations, no persist
///   • POST /api/v1/presets/{id}/apply     — apply a preset
///   • POST /api/v1/presets/custom/preview — Custom builder preview
///   • POST /api/v1/presets/custom/apply   — Custom builder apply
///
/// All endpoints are Admin-only and bootstrap-exempt (so applying a preset
/// that disables CAP-IDEN-CAPABILITY-ADMIN can't brick the install).
/// </summary>
[ApiController]
[Route("api/v1/presets")]
[Authorize(Roles = "Admin")]
[CapabilityBootstrap]
public class PresetsController(IMediator mediator) : ControllerBase
{
    /// <summary>Phase 4 Phase-G — Returns summary descriptors for all 8 presets.</summary>
    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<PresetSummaryResponseModel>>> List(CancellationToken ct)
    {
        var result = await mediator.Send(new GetPresetsQuery(), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-G — Returns the full descriptor for a single preset,
    /// with delta calculations vs catalog defaults and current install state.
    /// </summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<PresetDetailResponseModel>> Get(string id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetPresetDetailQuery(id), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-G — Side-by-side preset comparison. Body lists 2-4 preset
    /// IDs; response is a matrix with one row per capability and one column
    /// per preset.
    /// </summary>
    [HttpPost("compare")]
    public async Task<ActionResult<PresetCompareResponseModel>> Compare(
        [FromBody] PresetCompareRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(new ComparePresetsQuery(body.PresetIds ?? []), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-G — Stateless preview of a preset apply. Returns the
    /// deltas that would change and any constraint violations that would
    /// block the apply. No persistence.
    /// </summary>
    [HttpPost("{id}/preview-apply")]
    public async Task<ActionResult<PresetApplyPreviewResponseModel>> PreviewApply(
        string id,
        CancellationToken ct)
    {
        var result = await mediator.Send(new PreviewPresetApplyQuery(id), ct);
        if (result is null) return NotFound();
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-G — Apply a preset. Atomically flips capabilities to
    /// match the preset's enabled set via the bulk-toggle substrate; writes
    /// a single <c>PresetApplied</c> system audit row.
    ///
    /// No-op semantics: if install state already matches the preset, no
    /// capability rows mutate but the audit row is still written with
    /// <c>outcome = "no-op"</c> so the install records the re-assertion.
    /// </summary>
    [HttpPost("{id}/apply")]
    public async Task<ActionResult<PresetApplyResultResponseModel>> Apply(
        string id,
        [FromBody] PresetApplyRequestModel? body,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new ApplyPresetCommand(
                    PresetId: id,
                    Reason: body?.Reason,
                    IsCustomOverride: false,
                    CustomOverrides: null),
                ct);
            return Ok(result);
        }
        catch (CapabilityMutationException ex)
        {
            Response.ContentType = "application/problem+json";
            return new ObjectResult(ex.ToEnvelope())
            {
                StatusCode = ex.StatusCode,
            };
        }
    }

    /// <summary>
    /// Phase 4 Phase-G — Stateless preview of a Custom preset construction.
    /// Catalog defaults baseline + per-capability overrides → resulting set
    /// + constraint validation. No persistence.
    /// </summary>
    [HttpPost("custom/preview")]
    public async Task<ActionResult<PresetCustomPreviewResponseModel>> PreviewCustom(
        [FromBody] PresetCustomPreviewRequestModel body,
        CancellationToken ct)
    {
        var result = await mediator.Send(
            new PreviewCustomPresetCommand(body.CapabilityOverrides ?? []),
            ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-G — Apply a Custom preset (catalog defaults + user
    /// overrides). Audit row records as <c>PresetApplied</c> with
    /// <c>presetId = "PRESET-CUSTOM"</c> and override-count metadata.
    /// </summary>
    [HttpPost("custom/apply")]
    public async Task<ActionResult<PresetApplyResultResponseModel>> ApplyCustom(
        [FromBody] PresetCustomApplyRequestModel body,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(
                new ApplyPresetCommand(
                    PresetId: "PRESET-CUSTOM",
                    Reason: body.Reason,
                    IsCustomOverride: true,
                    CustomOverrides: body.CapabilityOverrides ?? []),
                ct);
            return Ok(result);
        }
        catch (CapabilityMutationException ex)
        {
            Response.ContentType = "application/problem+json";
            return new ObjectResult(ex.ToEnvelope())
            {
                StatusCode = ex.StatusCode,
            };
        }
    }
}
