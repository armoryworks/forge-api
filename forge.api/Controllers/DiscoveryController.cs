using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Capabilities.Discovery;
using Forge.Api.Features.Discovery.Apply;
using Forge.Api.Features.Discovery.GetQuestions;
using Forge.Api.Features.Discovery.Preview;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase 4 Phase-F — Discovery wizard endpoints. The wizard walks an admin
/// through ~22 questions, branches by size / regulation / multi-site,
/// produces a recommendation (preset + confidence + rationale + capability
/// deltas), and lets the admin preview deltas before applying.
///
/// Three endpoints:
///   • GET /questions — the question catalog (consultant mode optional)
///   • POST /preview — stateless recommendation preview (does NOT persist)
///   • POST /apply — persists a DiscoveryRun + applies the deltas atomically
///
/// All admin-only via [Authorize(Roles = "Admin")]; bootstrap-exempt
/// (capability-gating must not be able to brick discovery itself).
///
/// Returns Problem Details on capability mutation conflicts (412 for stale
/// ETag, 409 for dependency / mutex violations) — the apply endpoint
/// surfaces these by re-throwing the underlying
/// <see cref="CapabilityMutationException"/> from the bulk-toggle handler.
/// </summary>
[ApiController]
[Route("api/v1/discovery")]
[Authorize(Roles = "Admin")]
[CapabilityBootstrap]
public class DiscoveryController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 4 Phase-F — Returns the discovery question catalog. Consultant
    /// mode (per 4C decision #6) adds the per-branch deepdive questions.
    /// </summary>
    [HttpGet("questions")]
    public async Task<ActionResult<DiscoveryQuestionsResponseModel>> GetQuestions(
        [FromQuery] string? mode = null,
        CancellationToken ct = default)
    {
        var consultantMode = string.Equals(mode, "consultant", StringComparison.OrdinalIgnoreCase);
        var result = await mediator.Send(new GetDiscoveryQuestionsQuery(consultantMode), ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-F — Stateless recommendation preview. No persistence.
    /// Returns recommended preset + confidence + alternatives + rationale +
    /// capability deltas vs current install state.
    /// </summary>
    [HttpPost("preview")]
    public async Task<ActionResult<DiscoveryRecommendationResponseModel>> Preview(
        [FromBody] PreviewDiscoveryRecommendationCommand body,
        CancellationToken ct)
    {
        var result = await mediator.Send(body, ct);
        return Ok(result);
    }

    /// <summary>
    /// Phase 4 Phase-F — Apply a discovery recommendation. Persists a
    /// <c>DiscoveryRun</c> row and atomically applies the capability deltas
    /// via the bulk-toggle substrate.
    /// </summary>
    [HttpPost("apply")]
    public async Task<ActionResult<DiscoveryRecommendationResponseModel>> Apply(
        [FromBody] ApplyDiscoveryRecommendationCommand body,
        CancellationToken ct)
    {
        try
        {
            var result = await mediator.Send(body, ct);
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
