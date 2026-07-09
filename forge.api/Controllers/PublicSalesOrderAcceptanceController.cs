using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.SalesOrders.Acceptance;

namespace Forge.Api.Controllers;

/// <summary>
/// Public accept portal — anonymous, token-addressed endpoints the customer uses to review and accept a
/// Sales Order's terms (Forge-native e-sign for offline / air-gapped-friendly deals). The link token is
/// unguessable; acceptance additionally requires proving the second key. Covered by the global per-IP
/// rate limiter. Mirrors the public terms controller.
/// </summary>
[ApiController]
[Route("api/v1/public/so-acceptance")]
[AllowAnonymous]
[RequiresCapability("CAP-O2C-SO")]
public class PublicSalesOrderAcceptanceController(IMediator mediator) : ControllerBase
{
    [HttpGet("{token}")]
    public async Task<ActionResult<PublicAcceptanceViewModel>> Get(string token)
    {
        var result = await mediator.Send(new GetPublicAcceptanceQuery(token));
        return Ok(result);
    }

    [HttpPost("{token}/accept")]
    public async Task<IActionResult> Accept(string token, [FromBody] PublicAcceptRequest request)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();
        await mediator.Send(new SubmitPublicAcceptanceCommand(token, request.VerificationKey, request.AcceptedByName, ip));
        return NoContent();
    }
}

/// <summary>Body for a customer's public acceptance submission.</summary>
public record PublicAcceptRequest(string? VerificationKey, string AcceptedByName);
