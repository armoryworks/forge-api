using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Parts;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Where-used lookup for a part: the parents whose current BOM lists it and the open work
/// orders building each parent. Same roles and capability gate as the parts controller.
/// </summary>
[ApiController]
[Route("api/v1/parts/{partId:int}/where-used")]
[Authorize(Roles = "Admin,Manager,Engineer,ProductionWorker,PM,OfficeManager")]
[RequiresCapability("CAP-MD-PARTS")]
public class PartWhereUsedController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PartWhereUsedResponseModel>>> Get(int partId, CancellationToken ct)
        => Ok(await mediator.Send(new GetPartWhereUsedQuery(partId), ct));
}
