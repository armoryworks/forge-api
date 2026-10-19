using System.Text.Json;

using Fido2NetLib;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// WebAuthn/passkey ceremonies. Registration needs a signed-in session
/// (enroll on the desktop); the challenge pair runs mid-login off the
/// MFA-pending token, so the controller is bootstrap-exempt like the rest
/// of auth.
///
/// <para>The WebAuthn payloads bypass the app-wide MVC JSON options: the
/// global string-enum converter would otherwise override Fido2's own enum
/// converters (<c>public-key</c>, <c>required</c>, COSE algorithm ids). Options
/// go out as Fido2's own <c>ToJson()</c>, and the authenticator responses come
/// in as raw JSON deserialized with default options, so Fido2's type-level
/// converters apply.</para>
/// </summary>
[ApiController]
[Route("api/v1/auth/passkeys")]
[CapabilityBootstrap]
public class PasskeysController(IMediator mediator) : ControllerBase
{
    private string Origin => $"{Request.Scheme}://{Request.Host}";

    public record RegisterCompleteRequestModel(JsonElement Response, string? DeviceName);

    [HttpPost("register/options")]
    [Authorize]
    [Produces("application/json", Type = typeof(CredentialCreateOptions))]
    public async Task<IActionResult> RegisterOptions()
        => FidoContent((await mediator.Send(new BeginPasskeyRegistrationCommand(Origin))).ToJson());

    [HttpPost("register")]
    [Authorize]
    public async Task<ActionResult<object>> Register(
        [FromBody] RegisterCompleteRequestModel request)
    {
        var response = FidoJson.Read<AuthenticatorAttestationRawResponse>(request.Response);
        if (response is null) return BadRequest(new { message = "Malformed passkey registration response." });

        var name = await mediator.Send(new CompletePasskeyRegistrationCommand(
            Origin, response, request.DeviceName));
        return Ok(new { deviceName = name });
    }

    public record ChallengeOptionsRequestModel(string MfaPendingToken);

    [HttpPost("challenge/options")]
    [AllowAnonymous]
    [Produces("application/json", Type = typeof(AssertionOptions))]
    public async Task<IActionResult> ChallengeOptions(
        [FromBody] ChallengeOptionsRequestModel request)
    {
        var options = await mediator.Send(
            new BeginPasskeyMfaCommand(request.MfaPendingToken, Origin));
        if (options is null) return NotFound();
        return FidoContent(options.ToJson());
    }

    public record ChallengeValidateRequestModel(string MfaPendingToken, JsonElement Response);

    [HttpPost("challenge/validate")]
    [AllowAnonymous]
    public async Task<ActionResult<MfaValidateResponseModel>> ChallengeValidate(
        [FromBody] ChallengeValidateRequestModel request)
    {
        var response = FidoJson.Read<AuthenticatorAssertionRawResponse>(request.Response);
        if (response is null) return BadRequest(new { message = "Malformed passkey assertion response." });

        var result = await mediator.Send(new ValidatePasskeyMfaCommand(
            request.MfaPendingToken, Origin, response));
        if (result is null) return Unauthorized();
        return Ok(result);
    }

    private ContentResult FidoContent(string json) => Content(json, "application/json");
}
