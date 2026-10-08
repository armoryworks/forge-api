using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Parts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/parts")]
[Authorize(Roles = "Admin,Manager,Engineer,ProductionWorker,PM,OfficeManager")]
[RequiresCapability("CAP-QC-INSPECTION")]
public class PartQualityController(IMediator mediator) : ControllerBase
{
    [HttpGet("{id:int}/quality-summary")]
    public async Task<ActionResult<PartQualitySummaryResponseModel>> GetQualitySummary(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetPartQualitySummaryQuery(id), ct));
}
