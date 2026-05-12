using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.IoT;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/shop-floor/machine")]
[Authorize]
[RequiresCapability("CAP-MFG-MACHINE-CONNECT")]
public class ShopFloorMachineController(IMediator mediator) : ControllerBase
{
    [HttpGet("{workCenterId:int}/live")]
    public async Task<ActionResult<List<MachineDataPointResponseModel>>> GetLatestValues(int workCenterId)
    {
        var result = await mediator.Send(new GetMachineTagLatestQuery(workCenterId));
        return Ok(result);
    }

    [HttpGet("{workCenterId:int}/history")]
    public async Task<ActionResult<List<MachineDataPointResponseModel>>> GetHistory(
        int workCenterId,
        [FromQuery] int? tagId,
        [FromQuery] DateTimeOffset from,
        [FromQuery] DateTimeOffset to)
    {
        var result = await mediator.Send(new GetMachineTagHistoryQuery(workCenterId, tagId, from, to));
        return Ok(result);
    }
}
