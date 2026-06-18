using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Auth;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/auth")]
public class AuthController(IMediator mediator, ISsoHandoffStore handoffStore) : ControllerBase
{
    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Login(LoginCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public async Task<ActionResult<AuthUserResponseModel>> Me()
    {
        var result = await mediator.Send(new GetCurrentUserQuery(User));
        return Ok(result);
    }

    [HttpGet("status")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupStatusResponseModel>> Status()
    {
        var result = await mediator.Send(new CheckSetupStatusQuery());
        return Ok(result);
    }

    [HttpPost("setup")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> Setup(InitialSetupCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    // The first-run module picker reads this list (anonymous — it renders during
    // setup, before any admin exists). Static catalog data; no DB hit.
    [HttpGet("setup/modules")]
    [AllowAnonymous]
    public ActionResult<IReadOnlyList<SetupModuleResponseModel>> SetupModules()
    {
        var modules = Forge.Api.Capabilities.ModuleCatalog.All
            .Select(m => new SetupModuleResponseModel(m.Id, m.Name, m.Summary, m.PrerequisiteNote, m.DefaultSelected))
            .ToList();
        return Ok(modules);
    }

    [HttpGet("validate-token/{token}")]
    [AllowAnonymous]
    public async Task<ActionResult<SetupTokenInfoResponse>> ValidateToken(string token)
    {
        var result = await mediator.Send(new ValidateSetupTokenQuery(token));
        return Ok(result);
    }

    [HttpPost("complete-setup")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> CompleteSetup(CompleteSetupCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("set-pin")]
    [Authorize]
    public async Task<IActionResult> SetPin(SetPinCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }

    [HttpPost("kiosk-login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> KioskLogin(KioskLoginCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPost("nfc-login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> NfcLogin(NfcKioskLoginCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// Unified scan login — accepts any scan identifier (RFID, NFC, barcode, biometric).
    /// Checks UserScanIdentifiers first, then falls back to EmployeeBarcode field.
    /// </summary>
    [HttpPost("scan-login")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> ScanLogin(ScanLoginCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }

    [HttpPut("profile")]
    [Authorize]
    public async Task<ActionResult<AuthUserResponseModel>> UpdateProfile(UpdateProfileCommand command)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var result = await mediator.Send(command with { UserId = userId });
        return Ok(result);
    }

    [HttpPost("logout")]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value;
        await mediator.Send(new LogoutCommand(jti));
        return NoContent();
    }

    [HttpPost("refresh")]
    [Authorize]
    public async Task<ActionResult<LoginResponse>> Refresh()
    {
        var jti = User.FindFirst(JwtRegisteredClaimNames.Jti)?.Value
            ?? throw new UnauthorizedAccessException("Missing token identifier");
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        var result = await mediator.Send(new RefreshTokenCommand(jti, userId));
        return Ok(result);
    }

    [HttpPost("change-password")]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordCommand command)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await mediator.Send(command with { UserId = userId });
        return NoContent();
    }

    [HttpGet("sso/providers")]
    [AllowAnonymous]
    public async Task<IActionResult> GetSsoProviders()
        => Ok(await mediator.Send(new GetSsoProvidersQuery()));

    [HttpGet("sso/{provider}/login")]
    [AllowAnonymous]
    public IActionResult SsoLogin(string provider, [FromQuery] string? returnUrl)
    {
        var scheme = ResolveScheme(provider);
        var properties = new AuthenticationProperties
        {
            RedirectUri = $"/api/v1/auth/sso/{provider}/callback",
        };

        return Challenge(properties, scheme);
    }

    /// <summary>
    /// Token-for-token exchange. Caller presents a fresh external-provider
    /// id_token (Google, Microsoft / Azure AD v2.0, or a generic OIDC IdP
    /// configured per install) and receives a Forge JWT bound to the
    /// matched <c>ApplicationUser</c>. The id_token IS the credential —
    /// validated server-side against the provider's JWKS + the configured
    /// client id (audience, including any <c>AdditionalAudiences</c>).
    /// The browser-based OAuth flow is NOT involved.
    ///
    /// Intended for federated client apps (deployed alongside Forge) whose
    /// users already authenticated via the same IdP. The returned Forge JWT
    /// is used with the standard <c>Authorization: Bearer ...</c> on
    /// subsequent calls — Forge's actions are attributed to the real user,
    /// not to a service identity. Audit / activity rows therefore show the
    /// real person, the same as a browser-flow login would.
    ///
    /// Errors map via the standard middleware:
    ///   - 400 — request shape invalid (FluentValidation).
    ///   - 401 — id_token validation failed (bad signature, expired,
    ///     wrong audience, unverified email).
    ///   - 404 / 401 — no matching ApplicationUser
    ///     (<see cref="InvalidOperationException"/> from
    ///     <see cref="SsoCallbackHandler"/>).
    /// </summary>
    [HttpPost("sso/token-exchange")]
    [AllowAnonymous]
    public async Task<ActionResult<LoginResponse>> SsoTokenExchange(
        [FromBody] SsoTokenExchangeRequestModel request)
    {
        var result = await mediator.Send(
            new ExchangeSsoTokenCommand(request.Provider, request.IdToken));
        return Ok(result);
    }

    [HttpGet("sso/{provider}/callback")]
    [AllowAnonymous]
    public async Task<IActionResult> SsoCallback(string provider)
    {
        // Authenticate against the temporary cookie set during the OAuth round-trip
        var result = await HttpContext.AuthenticateAsync("SsoExternalCookie");

        if (!result.Succeeded)
            return Redirect("/sso/callback?error=sso_failed");

        var externalId = result.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? throw new InvalidOperationException("No external ID found in SSO claims");
        var email = result.Principal?.FindFirst(ClaimTypes.Email)?.Value
            ?? throw new InvalidOperationException("No email found in SSO claims");

        try
        {
            var loginResponse = await mediator.Send(new SsoCallbackCommand(provider, externalId, email));

            // Clean up the temporary external cookie
            await HttpContext.SignOutAsync("SsoExternalCookie");

            // Hand the session back via an opaque single-use code, NOT the JWT
            // itself — a token in the redirect URL leaks into proxy/nginx access
            // logs, the Referer header, and browser history. The SPA exchanges
            // this code (POST) for the real LoginResponse.
            var code = handoffStore.Create(loginResponse);
            return Redirect($"/sso/callback?code={code}");
        }
        catch (SsoDomainNotPermittedException)
        {
            // Per-install AllowedDomains policy excludes this email's domain.
            // Redirect to the SSO landing with a distinct error code so the
            // UI can render a tailored "your account isn't permitted" message
            // (vs the generic "no account found" path below).
            await HttpContext.SignOutAsync("SsoExternalCookie");
            return Redirect("/sso/callback?error=domain_not_permitted");
        }
        catch (InvalidOperationException)
        {
            await HttpContext.SignOutAsync("SsoExternalCookie");
            return Redirect("/sso/callback?error=no_account");
        }
    }

    /// <summary>
    /// Exchanges the opaque single-use code from the browser SSO callback
    /// redirect for the real <see cref="LoginResponse"/>. The code is consumed
    /// on first use; an unknown / expired / already-used code returns 401.
    /// This is what keeps the JWT out of the callback URL.
    /// </summary>
    [HttpPost("sso/exchange-code")]
    [AllowAnonymous]
    public ActionResult<LoginResponse> ExchangeSsoCode([FromBody] SsoExchangeCodeRequestModel request)
    {
        var response = handoffStore.Consume(request.Code);
        if (response is null)
            return Unauthorized(new { message = "Invalid or expired SSO code." });
        return Ok(response);
    }

    [HttpPost("sso/link")]
    [Authorize]
    public async Task<IActionResult> LinkSso([FromBody] LinkSsoIdentityCommand command)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await mediator.Send(command with { UserId = userId });
        return NoContent();
    }

    [HttpDelete("sso/unlink/{provider}")]
    [Authorize]
    public async Task<IActionResult> UnlinkSso(string provider)
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        await mediator.Send(new UnlinkSsoIdentityCommand(userId, provider));
        return NoContent();
    }

    [HttpGet("sso/linked")]
    [Authorize]
    public async Task<IActionResult> GetLinkedProviders()
    {
        var userId = int.Parse(User.FindFirst(ClaimTypes.NameIdentifier)!.Value);
        return Ok(await mediator.Send(new GetLinkedSsoProvidersQuery(userId)));
    }

    // ── MFA Setup (authenticated user) ──────────────────────

    [HttpPost("mfa/setup")]
    [Authorize]
    public async Task<ActionResult<MfaSetupResponseModel>> BeginMfaSetup([FromBody] BeginMfaSetupRequest? request)
    {
        var result = await mediator.Send(new BeginMfaSetupCommand(User, request?.DeviceName));
        return Ok(result);
    }

    [HttpPost("mfa/verify-setup")]
    [Authorize]
    public async Task<IActionResult> VerifyMfaSetup([FromBody] MfaVerifySetupRequestModel request)
    {
        var result = await mediator.Send(new VerifyMfaSetupCommand(User, request.DeviceId, request.Code));
        if (!result) return BadRequest(new { message = "Invalid verification code" });
        return Ok(new { verified = true });
    }

    [HttpDelete("mfa/disable")]
    [Authorize]
    public async Task<IActionResult> DisableMfa()
    {
        await mediator.Send(new DisableMfaCommand(User));
        return NoContent();
    }

    [HttpDelete("mfa/devices/{deviceId:int}")]
    [Authorize]
    public async Task<IActionResult> RemoveMfaDevice(int deviceId)
    {
        await mediator.Send(new RemoveMfaDeviceCommand(User, deviceId));
        return NoContent();
    }

    [HttpGet("mfa/status")]
    [Authorize]
    public async Task<ActionResult<MfaStatusResponseModel>> GetMfaStatus()
    {
        var result = await mediator.Send(new GetMfaStatusQuery(User));
        return Ok(result);
    }

    // ── MFA Login Flow (partially authenticated) ──────────

    [HttpPost("mfa/challenge")]
    [AllowAnonymous]
    public async Task<ActionResult<MfaChallengeResponseModel>> CreateMfaChallenge([FromBody] MfaChallengeRequest request)
    {
        var result = await mediator.Send(new CreateMfaChallengeCommand(request.MfaPendingToken));
        return Ok(result);
    }

    [HttpPost("mfa/validate")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateMfaChallenge([FromBody] MfaValidateRequestModel request)
    {
        var result = await mediator.Send(new ValidateMfaChallengeCommand(request.ChallengeToken, request.Code, request.RememberDevice));
        if (result is null) return Unauthorized(new { message = "Invalid MFA code" });
        return Ok(result);
    }

    [HttpPost("mfa/recovery")]
    [AllowAnonymous]
    public async Task<IActionResult> ValidateMfaRecovery([FromBody] MfaRecoveryLoginRequestModel request)
    {
        var result = await mediator.Send(new ValidateMfaRecoveryCommand(request.ChallengeToken, request.RecoveryCode));
        if (result is null) return Unauthorized(new { message = "Invalid recovery code" });
        return Ok(result);
    }

    [HttpPost("mfa/recovery-codes")]
    [Authorize]
    public async Task<ActionResult<MfaRecoveryCodesResponseModel>> GenerateRecoveryCodes()
    {
        var result = await mediator.Send(new GenerateRecoveryCodesCommand(User));
        return Ok(result);
    }

    private static string ResolveScheme(string provider) => provider switch
    {
        "google" => GoogleDefaults.AuthenticationScheme,
        "microsoft" => MicrosoftAccountDefaults.AuthenticationScheme,
        "oidc" => OpenIdConnectDefaults.AuthenticationScheme,
        _ => throw new InvalidOperationException($"Unknown SSO provider: {provider}")
    };
}

public record BeginMfaSetupRequest(string? DeviceName);
// F-054: challenge is gated on the MFA-pending token (proof the password step
// passed), not a caller-supplied userId. The bound userId is read from the token.
public record MfaChallengeRequest(string MfaPendingToken);
