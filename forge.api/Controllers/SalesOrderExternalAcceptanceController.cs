using MediatR;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Authentication;
using Forge.Api.Capabilities;
using Forge.Api.Features.SalesOrders.Acceptance;

namespace Forge.Api.Controllers;

/// <summary>
/// External acceptance channel — lets a customer's own system record acceptance of a Sales Order's
/// terms. Accepts both the standard JWT scheme and the user-bound System API key scheme, so a headless
/// integration client (a service user holding a System API key) can post acceptance unattended. Kept on
/// its own controller so only this narrow endpoint is reachable by API keys, not the whole SO surface.
/// See docs/api-key-integrations.md.
/// </summary>
[ApiController]
[Route("api/v1/orders/{salesOrderId:int}/acceptance")]
[Authorize(
    AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme + "," + SystemApiKeyAuthenticationOptions.SchemeName,
    Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-SO")]
public class SalesOrderExternalAcceptanceController(IMediator mediator) : ControllerBase
{
    [HttpPost("external")]
    public async Task<ActionResult<SalesOrderAcceptanceResponseModel>> RecordExternal(
        int salesOrderId, [FromBody] RecordExternalAcceptanceRequest request)
    {
        var result = await mediator.Send(new RecordExternalAcceptanceCommand(
            salesOrderId, request.Reference, request.AcceptedByName, request.Note));
        return Ok(result);
    }
}

/// <summary>Body for an external-system acceptance push.</summary>
public record RecordExternalAcceptanceRequest(string? Reference, string? AcceptedByName, string? Note);
