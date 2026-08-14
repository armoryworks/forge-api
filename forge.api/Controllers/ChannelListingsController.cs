using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.ChannelListings;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Listings published on a sales channel, and the mapping that connects them to
/// parts. The unmapped filter is the triage queue.
/// </summary>
[ApiController]
[Route("api/v1/channel-listings")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer")]
[RequiresCapability("CAP-EXT-ECOMMERCE")]
public class ChannelListingsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PagedResponse<ChannelListingResponseModel>>> GetListings(
        [FromQuery] ChannelListingListQuery query, CancellationToken ct)
    {
        var result = await mediator.Send(new GetChannelListingsQuery(query), ct);
        return Ok(result);
    }

    /// <summary>
    /// Map a listing to a part, or clear the mapping by omitting partId.
    /// Setting a mapping back-fills any already-imported order lines for the
    /// same SKU that have no part yet — mapping only helps future orders
    /// otherwise, and the backlog is the point.
    /// </summary>
    [HttpPut("{id:int}/mapping")]
    public async Task<ActionResult<MapChannelListingResult>> MapListing(
        int id, [FromBody] MapChannelListingRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new MapChannelListingCommand(id, model.PartId), ct);
        return Ok(result);
    }
}
