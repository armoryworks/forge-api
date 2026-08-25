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
    public async Task<ActionResult<ScanResolveResponseModel>> ResolveScan(
        [FromBody] ScanResolveRequestModel request)
        => Ok(await mediator.Send(new ResolveScanQuery(request.Code)));

    [HttpGet("jobs/{id:int}/status")]
    public async Task<ActionResult<JobStatusResponseModel>> JobStatus(int id)
        => Ok(await mediator.Send(new GetJobStatusQuery(id)));

    public record AdvanceRequestModel(string? ScanCode);

    [HttpPost("jobs/{id:int}/advance")]
    public async Task<ActionResult<JobAdvanceResponseModel>> Advance(
        int id, [FromBody] AdvanceRequestModel request)
        => Ok(await mediator.Send(new AdvanceJobCommand(id, DeviceKey(), request.ScanCode)));

    private string DeviceKey()
    {
        if (Request.Headers.TryGetValue(DeviceHeader, out var uuid) && !string.IsNullOrWhiteSpace(uuid))
            return uuid.ToString();
        if (HttpContext.Items[SharedDeviceMiddleware.ItemKey] is int deviceId)
            return $"shared:{deviceId}";
        return $"user:{User.FindFirstValue(ClaimTypes.NameIdentifier)}";
    }
}
