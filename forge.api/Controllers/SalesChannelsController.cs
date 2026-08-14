using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.SalesChannels;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Sales-channel administration. Channels are install configuration rather than
/// transactional data — they decide where orders route, which account carries a
/// receivable, and who is liable for the tax — so mutation is Admin/Manager only
/// while the list is readable by the roles that create orders.
/// </summary>
[ApiController]
[Route("api/v1/sales-channels")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-CHANNELS")]
public class SalesChannelsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SalesChannelResponseModel>>> GetChannels(
        [FromQuery] bool includeInactive,
        [FromQuery] SalesChannelType? channelType,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesChannelsQuery(includeInactive, channelType), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesChannelResponseModel>> GetChannel(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesChannelByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SalesChannelResponseModel>> CreateChannel(
        [FromBody] CreateSalesChannelRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateSalesChannelCommand(model), ct);
        return CreatedAtAction(nameof(GetChannel), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<SalesChannelResponseModel>> UpdateChannel(
        int id, [FromBody] UpdateSalesChannelRequestModel model, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateSalesChannelCommand(id, model), ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/set-default")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> SetDefault(int id, CancellationToken ct)
    {
        await mediator.Send(new SetDefaultSalesChannelCommand(id), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteChannel(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteSalesChannelCommand(id), ct);
        return NoContent();
    }
}
