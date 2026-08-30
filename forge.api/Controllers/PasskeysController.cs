using Fido2NetLib;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Auth;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// WebAuthn/passkey ceremonies. Registration needs a signed-in session
/// (enroll on the desktop); the challenge pair runs mid-login off the
/// MFA-pending token, so the controller is bootstrap-exempt like the rest
/// of auth.
/// </summary>
[ApiController]
[Route("api/v1/auth/passkeys")]
[CapabilityBootstrap]
public class PasskeysController(IMediator mediator) : ControllerBase
{
    private string Origin => $"{Request.Scheme}://{Request.Host}";

    public record RegisterCompleteRequestModel(
        AuthenticatorAttestationRawResponse Response, string? DeviceName);

    [HttpPost("register/options")]
    [Authorize]
    public async Task<ActionResult<CredentialCreateOptions>> RegisterOptions()
        => Ok(await mediator.Send(new BeginPasskeyRegistrationCommand(Origin)));

    [HttpPost("register")]
    [Authorize]
    public async Task<ActionResult<object>> Register(
        [FromBody] RegisterCompleteRequestModel request)
    {
        var name = await mediator.Send(new CompletePasskeyRegistrationCommand(
            Origin, request.Response, request.DeviceName));
        return Ok(new { deviceName = name });
    }

    public record ChallengeOptionsRequestModel(string MfaPendingToken);

    [HttpPost("challenge/options")]
    [AllowAnonymous]
    public async Task<ActionResult<AssertionOptions>> ChallengeOptions(
        [FromBody] ChallengeOptionsRequestModel request)
    {
        var options = await mediator.Send(
            new BeginPasskeyMfaCommand(request.MfaPendingToken, Origin));
        if (options is null) return NotFound();
        return Ok(options);
    }

    public record ChallengeValidateRequestModel(
        string MfaPendingToken, AuthenticatorAssertionRawResponse Response);

    [HttpPost("challenge/validate")]
    [AllowAnonymous]
    public async Task<ActionResult<MfaValidateResponseModel>> ChallengeValidate(
        [FromBody] ChallengeValidateRequestModel request)
    {
        var result = await mediator.Send(new ValidatePasskeyMfaCommand(
            request.MfaPendingToken, Origin, request.Response));
        if (result is null) return Unauthorized();
        return Ok(result);
    }
}
