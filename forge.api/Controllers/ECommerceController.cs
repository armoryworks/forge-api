using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.ECommerce;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Storefront and marketplace connector administration.
///
/// <para>Gated on CAP-EXT-ECOMMERCE, which had no gate at all before — a
/// straight violation of the rule that every endpoint reuses or registers a
/// capability. It matters here more than most: these endpoints hold store
/// credentials and move buyer data across a trust boundary.</para>
/// </summary>
[ApiController]
[Route("api/v1/admin/ecommerce")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-EXT-ECOMMERCE")]
public class ECommerceController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ECommerceIntegrationResponseModel>>> GetIntegrations()
    {
        var result = await mediator.Send(new GetECommerceIntegrationsQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ECommerceIntegrationResponseModel>> CreateIntegration(
        [FromBody] CreateECommerceIntegrationRequestModel model)
    {
        var result = await mediator.Send(new CreateECommerceIntegrationCommand(model));
        return CreatedAtAction(nameof(GetIntegrations), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ECommerceIntegrationResponseModel>> UpdateIntegration(
        int id, [FromBody] UpdateECommerceIntegrationRequestModel model)
    {
        var result = await mediator.Send(new UpdateECommerceIntegrationCommand(id, model));
        return Ok(result);
    }

    [HttpPost("{id:int}/test")]
    public async Task<ActionResult<TestECommerceConnectionResult>> TestConnection(int id)
    {
        var result = await mediator.Send(new TestECommerceConnectionCommand(id));
        return Ok(result);
    }

    /// <summary>
    /// Poll a channel's connector and import what it returns as retail orders.
    ///
    /// <para>Keyed on the CHANNEL, not the integration: the channel is what
    /// carries the house account the receivable lands on and the tax treatment
    /// the orders inherit, so importing without one has nowhere to put the
    /// money.</para>
    /// </summary>
    [HttpPost("channels/{channelId:int}/import")]
    public async Task<ActionResult<List<ECommerceOrderSyncResponseModel>>> ImportOrders(
        int channelId, [FromQuery] DateTimeOffset? since, CancellationToken ct)
    {
        var result = await mediator.Send(new ImportChannelOrdersCommand(channelId, since), ct);
        return Ok(result);
    }

    /// <summary>Poll a channel's listings and upsert them for part mapping and inventory sync.</summary>
    [HttpPost("channels/{channelId:int}/sync-listings")]
    public async Task<ActionResult<SyncChannelListingsResult>> SyncListings(int channelId, CancellationToken ct)
    {
        var result = await mediator.Send(new SyncChannelListingsCommand(channelId), ct);
        return Ok(result);
    }

    /// <summary>Poll a marketplace channel's payout batches and reconcile them against orders.</summary>
    [HttpPost("channels/{channelId:int}/import-settlements")]
    [RequiresCapability("CAP-O2C-SETTLEMENT")]
    public async Task<ActionResult<ImportChannelSettlementsResult>> ImportSettlements(
        int channelId, [FromQuery] DateTimeOffset? since, CancellationToken ct)
    {
        var result = await mediator.Send(new ImportChannelSettlementsCommand(channelId, since), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/syncs")]
    public async Task<ActionResult<List<ECommerceOrderSyncResponseModel>>> GetOrderSyncs(int id)
    {
        var result = await mediator.Send(new GetECommerceOrderSyncsQuery(id));
        return Ok(result);
    }

    [HttpPost("syncs/{syncId:int}/retry")]
    public async Task<ActionResult<ECommerceOrderSyncResponseModel>> RetryImport(int syncId)
    {
        var result = await mediator.Send(new RetryECommerceImportCommand(syncId));
        return Ok(result);
    }
}
