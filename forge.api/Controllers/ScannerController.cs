using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Scanner;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/scanner")]
[Authorize]
[RequiresCapability("CAP-MFG-SHOPFLOOR")]
public class ScannerController(IMediator mediator) : ControllerBase
{
    [HttpGet("context/{partIdentifier}")]
    public async Task<ActionResult<ScanContextResponseModel>> GetContext(string partIdentifier)
    {
        var result = await mediator.Send(new GetScanContextQuery(partIdentifier));
        return Ok(result);
    }

    [HttpPost("move")]
    public async Task<ActionResult<int>> Move([FromBody] ScanMoveRequestModel request)
    {
        var result = await mediator.Send(new ExecuteScanMoveCommand(request));
        return Created($"/api/v1/scanner/log", result);
    }

    [HttpPost("count")]
    public async Task<ActionResult<int>> Count([FromBody] ScanCountRequestModel request)
    {
        var result = await mediator.Send(new ExecuteScanCountCommand(request));
        return Created($"/api/v1/scanner/log", result);
    }

    [HttpPost("receive")]
    public async Task<ActionResult<int>> Receive([FromBody] ScanReceiveRequestModel request)
    {
        var result = await mediator.Send(new ExecuteScanReceiveCommand(request));
        return Created($"/api/v1/scanner/log", result);
    }

    [HttpPost("issue")]
    public async Task<ActionResult<int>> Issue([FromBody] ScanIssueRequestModel request)
    {
        var result = await mediator.Send(new ExecuteScanIssueCommand(request));
        return Created($"/api/v1/scanner/log", result);
    }

    [HttpPost("reverse")]
    public async Task<IActionResult> Reverse([FromBody] ScanReversalRequestModel request)
    {
        await mediator.Send(new ReverseScanActionCommand(request));
        return NoContent();
    }

    [HttpGet("log")]
    public async Task<ActionResult<List<ScanLogEntryModel>>> GetLog(
        [FromQuery] int? userId,
        [FromQuery] DateTimeOffset? date,
        [FromQuery] ScanActionType? actionType)
    {
        var result = await mediator.Send(new GetScanLogQuery(userId, date, actionType));
        return Ok(result);
    }

    [HttpGet("devices")]
    public async Task<ActionResult<List<ScanDeviceResponseModel>>> GetDevices()
    {
        var result = await mediator.Send(new GetScanDevicesQuery());
        return Ok(result);
    }

    [HttpPost("devices")]
    public async Task<ActionResult<ScanDeviceResponseModel>> PairDevice([FromBody] ScanDeviceRequestModel request)
    {
        var result = await mediator.Send(new PairScanDeviceCommand(request));
        return Created($"/api/v1/scanner/devices/{result.Id}", result);
    }

    [HttpDelete("devices/{id:int}")]
    public async Task<IActionResult> UnpairDevice(int id)
    {
        await mediator.Send(new UnpairScanDeviceCommand(id));
        return NoContent();
    }
}
