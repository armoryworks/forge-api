using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Mobile;
using Forge.Api.Middleware;

namespace Forge.Api.Controllers;

/// <summary>
/// The phone's five-screen surface: scan resolution, the job status card,
/// and scan-driven actions with duplicate collapse. Every mutation should
/// arrive with an Idempotency-Key (handled by middleware) and the device's
/// X-Device-Uuid so duplicate scans collapse per device.
/// </summary>
[ApiController]
[Route("api/v1/mobile")]
[Authorize]
[RequiresCapability("CAP-MOBILE-CORE")]
public class MobileController(IMediator mediator) : ControllerBase
{
    public const string DeviceHeader = "X-Device-Uuid";

    public record ScanResolveRequestModel(string Code);

    [HttpPost("scan/resolve")]
    [RequiresCapability("CAP-MOBILE-SCAN")]
    public async Task<ActionResult<ScanResolveResponseModel>> ResolveScan(
        [FromBody] ScanResolveRequestModel request)
        => Ok(await mediator.Send(new ResolveScanQuery(request.Code)));

    [HttpGet("jobs/{id:int}/status")]
    [RequiresCapability("CAP-MOBILE-JOBS")]
    public async Task<ActionResult<JobStatusResponseModel>> JobStatus(int id)
        => Ok(await mediator.Send(new GetJobStatusQuery(id)));

    public record AdvanceRequestModel(string? ScanCode);

    [HttpPost("jobs/{id:int}/advance")]
    [RequiresCapability("CAP-MOBILE-JOBS")]
    public async Task<ActionResult<JobAdvanceResponseModel>> Advance(
        int id, [FromBody] AdvanceRequestModel request)
        => Ok(await mediator.Send(new AdvanceJobCommand(id, DeviceKey(), request.ScanCode)));

    [HttpGet("lookup")]
    [RequiresCapability("CAP-MOBILE-LOOKUP")]
    public async Task<ActionResult<List<ScanResolveResponseModel>>> Lookup([FromQuery] string q)
        => Ok(await mediator.Send(new LookupQuery(q ?? string.Empty)));

    [HttpGet("clock/state")]
    [RequiresCapability("CAP-MOBILE-CLOCK")]
    public async Task<ActionResult<ClockStateResponseModel>> ClockState()
        => Ok(await mediator.Send(new GetClockStateQuery()));

    public record ClockPunchRequestModel(string EventType);

    [HttpPost("clock/punch")]
    [RequiresCapability("CAP-MOBILE-CLOCK")]
    public async Task<ActionResult<ClockPunchResponseModel>> ClockPunch([FromBody] ClockPunchRequestModel request)
        => Ok(await mediator.Send(new RecordClockPunchCommand(request.EventType)));

    [HttpDelete("clock/events/{eventId:int}")]
    [RequiresCapability("CAP-MOBILE-CLOCK")]
    public async Task<ActionResult<ClockStateResponseModel>> UndoClockPunch(int eventId)
        => Ok(await mediator.Send(new UndoClockPunchCommand(eventId)));

    [HttpGet("stock/on-hand")]
    [RequiresCapability("CAP-MOBILE-STOCK")]
    public async Task<ActionResult<OnHandResponseModel>> OnHand([FromQuery] int partId, [FromQuery] int locationId)
        => Ok(await mediator.Send(new GetOnHandQuery(partId, locationId)));

    public record MoveStockRequestModel(int PartId, int FromLocationId, int ToLocationId, decimal Quantity, string? LotNumber);

    [HttpPost("stock/move")]
    [RequiresCapability("CAP-MOBILE-STOCK")]
    public async Task<ActionResult<StockMoveResponseModel>> MoveStock([FromBody] MoveStockRequestModel request)
        => Ok(await mediator.Send(new MoveStockCommand(
            request.PartId, request.FromLocationId, request.ToLocationId, request.Quantity, request.LotNumber, DeviceKey())));

    private string DeviceKey()
    {
        if (Request.Headers.TryGetValue(DeviceHeader, out var uuid) && !string.IsNullOrWhiteSpace(uuid))
            return uuid.ToString();
        if (HttpContext.Items[SharedDeviceMiddleware.ItemKey] is int deviceId)
            return $"shared:{deviceId}";
        return $"user:{User.FindFirstValue(ClaimTypes.NameIdentifier)}";
    }

    [HttpPost("problem-reports")]
    public async Task<IActionResult> ReportProblem([FromBody] ReportProblemRequestModel request)
    {
        await mediator.Send(new ReportProblemCommand(request, DeviceKey()));
        return Accepted();
    }
}
