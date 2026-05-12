using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Replenishment;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/replenishment")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-PLAN-SAFETYSTOCK")]
public class ReplenishmentController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    // ── Burn Rates ──

    [HttpGet("burn-rates")]
    public async Task<ActionResult<List<BurnRateResponseModel>>> GetBurnRates(
        [FromQuery] string? search,
        [FromQuery] bool needsReorderOnly = false,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetBurnRatesQuery(search, needsReorderOnly), ct);
        return Ok(result);
    }

    // ── Reorder Suggestions ──

    [HttpGet("suggestions")]
    public async Task<ActionResult<List<ReorderSuggestionResponseModel>>> GetSuggestions(
        [FromQuery] ReorderSuggestionStatus? status = null,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetReorderSuggestionsQuery(status), ct);
        return Ok(result);
    }

    [HttpPost("suggestions/{id:int}/approve")]
    public async Task<ActionResult<ReorderSuggestionResponseModel>> Approve(
        int id, CancellationToken ct)
    {
        var result = await mediator.Send(new ApproveSuggestionCommand(id, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost("suggestions/approve-bulk")]
    public async Task<ActionResult<BulkApproveResult>> ApproveBulk(
        [FromBody] ApproveBulkRequest request, CancellationToken ct)
    {
        var result = await mediator.Send(
            new ApproveBulkSuggestionsCommand(request.SuggestionIds, GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost("suggestions/{id:int}/dismiss")]
    public async Task<IActionResult> Dismiss(
        int id, [FromBody] DismissRequest request, CancellationToken ct)
    {
        await mediator.Send(new DismissSuggestionCommand(id, GetUserId(), request.Reason), ct);
        return NoContent();
    }
}

public record ApproveBulkRequest(List<int> SuggestionIds);
public record DismissRequest(string Reason);
