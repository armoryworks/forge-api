using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.ECommerce;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Marketplace payout batches and their reconciliation state. Read-mostly: the
/// batches themselves arrive through the connector import, and the only
/// mutation is signing off on a variance that will never resolve on its own.
///
/// <para>⚡ ACCOUNTING BOUNDARY — the settlement record is operational and
/// app-resident in every mode, because the connector is what writes it. What
/// changes by mode is where the resulting journal lives.</para>
/// </summary>
[ApiController]
[Route("api/v1/channel-settlements")]
[Authorize(Roles = "Admin,Manager,Controller,OfficeManager")]
[RequiresCapability("CAP-O2C-SETTLEMENT")]
public class ChannelSettlementsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ChannelSettlementResponseModel>>> GetSettlements(
        [FromQuery] ChannelSettlementListQuery query, CancellationToken ct)
    {
        var result = await mediator.Send(new GetChannelSettlementsQuery(query), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ChannelSettlementDetailResponseModel>> GetSettlement(
        int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetChannelSettlementByIdQuery(id), ct);
        return Ok(result);
    }

    /// <summary>
    /// Accept a variance that will not resolve. Requires a written reason, and a
    /// later re-import will not overwrite the decision.
    /// </summary>
    [HttpPost("{id:int}/accept")]
    [Authorize(Roles = "Admin,Manager,Controller")]
    public async Task<ActionResult<ChannelSettlementResponseModel>> AcceptVariance(
        int id, [FromBody] AcceptChannelSettlementRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new AcceptChannelSettlementCommand(id, model.ResolutionNotes), ct);
        return Ok(result);
    }
}
