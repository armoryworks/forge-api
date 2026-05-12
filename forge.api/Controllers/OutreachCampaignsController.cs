using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Campaigns;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase 1r / Batch 5 — outreach-campaign CRUD. Gated under the same
/// CAP-O2C-LEAD capability as the leads controller; no separate
/// capability since campaigns are an inseparable feature of high-volume
/// lead intake.
/// </summary>
[ApiController]
[Route("api/v1/outreach-campaigns")]
[Authorize(Roles = "Admin,Manager,PM")]
[RequiresCapability("CAP-O2C-LEAD")]
public class OutreachCampaignsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<OutreachCampaignResponseModel>>> Get([FromQuery] bool? activeOnly)
        => Ok(await mediator.Send(new GetOutreachCampaignsQuery(activeOnly)));

    [HttpPost]
    public async Task<ActionResult<OutreachCampaignResponseModel>> Create([FromBody] CreateOutreachCampaignRequest request)
    {
        var result = await mediator.Send(new CreateOutreachCampaignCommand(request));
        return Created($"/api/v1/outreach-campaigns/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<OutreachCampaignResponseModel>> Update(int id, [FromBody] UpdateOutreachCampaignRequest request)
        => Ok(await mediator.Send(new UpdateOutreachCampaignCommand(id, request)));
}
