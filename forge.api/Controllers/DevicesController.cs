using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Devices;

namespace Forge.Api.Controllers;

/// <summary>
/// Mobile device lifecycle: admin-issued enrollment tokens, the anonymous
/// enroll/refresh exchange the app calls, and the device registry with
/// rename and revoke. Everything is gated on CAP-MOBILE-CORE — an instance
/// that hasn't turned the mobile app on accepts no enrollments.
/// </summary>
[ApiController]
[Route("api/v1/devices")]
[RequiresCapability("CAP-MOBILE-CORE")]
public class DevicesController(IMediator mediator) : ControllerBase
{
    public record CreateEnrollmentTokenRequestModel(int TargetUserId, bool IsShared = false);

    [HttpPost("enrollment-tokens")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<EnrollmentTokenResponseModel>> CreateEnrollmentToken(
        [FromBody] CreateEnrollmentTokenRequestModel request)
    {
        var result = await mediator.Send(
            new CreateEnrollmentTokenCommand(request.TargetUserId, request.IsShared));
        return Ok(result);
    }

    public record EnrollRequestModel(
        string Token, string DeviceUuid, string DeviceName, string Platform,
        string? OsVersion = null, string? AppVersion = null);

    [HttpPost("enroll")]
    [AllowAnonymous]
    public async Task<ActionResult<MobileAuthResponseModel>> Enroll(
        [FromBody] EnrollRequestModel request)
    {
        var result = await mediator.Send(new EnrollDeviceCommand(
            request.Token, request.DeviceUuid, request.DeviceName,
            request.Platform, request.OsVersion, request.AppVersion));
        return Ok(result);
    }

    public record EnrollOwnRequestModel(
        string DeviceUuid, string DeviceName, string Platform,
        string? OsVersion = null, string? AppVersion = null);

    [HttpPost("enroll-mine")]
    [Authorize]
    public async Task<ActionResult<MobileAuthResponseModel>> EnrollMine(
        [FromBody] EnrollOwnRequestModel request)
    {
        var result = await mediator.Send(new EnrollOwnDeviceCommand(
            request.DeviceUuid, request.DeviceName, request.Platform,
            request.OsVersion, request.AppVersion));
        return Ok(result);
    }

    public record RefreshRequestModel(string RefreshToken, string DeviceUuid);

    [HttpPost("refresh")]
    [AllowAnonymous]
    public async Task<ActionResult<MobileAuthResponseModel>> Refresh(
        [FromBody] RefreshRequestModel request)
    {
        var result = await mediator.Send(
            new RefreshDeviceTokenCommand(request.RefreshToken, request.DeviceUuid));
        return Ok(result);
    }

    [HttpGet]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<DeviceResponseModel>>> List([FromQuery] int? userId)
        => Ok(await mediator.Send(new ListDevicesQuery(userId)));

    [HttpGet("mine")]
    [Authorize]
    public async Task<ActionResult<List<DeviceResponseModel>>> Mine()
        => Ok(await mediator.Send(new ListMyDevicesQuery()));

    [HttpGet("lock-policy")]
    [Authorize]
    public async Task<ActionResult<LockPolicyResponseModel>> LockPolicy()
        => Ok(await mediator.Send(new GetLockPolicyQuery()));

    public record RenameRequestModel(string Name);

    [HttpPatch("{id:int}")]
    [Authorize]
    public async Task<IActionResult> Rename(int id, [FromBody] RenameRequestModel request)
    {
        await mediator.Send(new RenameDeviceCommand(id, request.Name));
        return NoContent();
    }

    [HttpPost("{id:int}/revoke")]
    [Authorize]
    public async Task<IActionResult> Revoke(int id)
    {
        await mediator.Send(new RevokeDeviceCommand(id));
        return NoContent();
    }
}
