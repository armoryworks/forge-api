using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.CustomerPortal;
using Forge.Api.Features.Terminology;
using Forge.Api.Services;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Phase 1q — customer-facing self-service portal. The two auth endpoints
/// (`request-link`, `exchange`) are <see cref="AllowAnonymous"/> +
/// <see cref="CapabilityBootstrapAttribute"/>-equivalent — they route
/// through the gate via <see cref="RequiresCapabilityAttribute"/> on the
/// controller, but anonymous portal sign-in must be reachable when the
/// capability is enabled. `terminology` is anonymous too, like the brand
/// settings read: it returns only the renames of the fixed term catalog (no
/// other terminology rows) so the portal, login page included, shows the
/// same terms as the employee app. The /me/*
/// endpoints require a portal session JWT validated by the dedicated
/// <see cref="PortalTokenService.SchemeName"/> bearer scheme
/// (`portal_session=true` claim); employee tokens are not accepted there.
/// The two auth endpoints also run under <see cref="PortalAuthRateLimit"/>,
/// and the /me list endpoints report their full row count in
/// <see cref="PortalReadRules.TotalCountHeader"/>.
/// </summary>
[ApiController]
[Route("api/v1/portal")]
[RequiresCapability("CAP-EXT-CUSTOMER-PORTAL")]
public class CustomerPortalController(IMediator mediator, IConfiguration config) : ControllerBase
{
    [HttpPost("auth/request-link")]
    [AllowAnonymous]
    public async Task<ActionResult<RequestMagicLinkResult>> RequestLink([FromBody] RequestMagicLinkRequest request)
    {
        var result = await mediator.Send(
            new RequestMagicLinkCommand(request.Email, FrontendBaseUrl.Resolve(config, Request)));
        return Ok(result);
    }

    [HttpPost("auth/exchange")]
    [AllowAnonymous]
    public async Task<ActionResult<PortalSessionResponseModel>> Exchange([FromBody] ExchangeRequest request)
    {
        var result = await mediator.Send(new ExchangeMagicLinkCommand(request.Token));
        return Ok(result);
    }

    [HttpGet("terminology")]
    [AllowAnonymous]
    public async Task<ActionResult<List<TerminologyEntryResponseModel>>> Terminology()
    {
        var result = await mediator.Send(new GetPortalTerminologyQuery());
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public ActionResult<PortalIdentityModel> Me()
    {
        var identity = ResolvePortalIdentity();
        return Ok(identity);
    }

    [HttpGet("me/dashboard")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public async Task<ActionResult<PortalSummaryResponseModel>> Dashboard()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalDashboardQuery(customerId));
        return Ok(result);
    }

    [HttpGet("me/sales-orders")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public async Task<ActionResult<List<PortalSalesOrderListItem>>> SalesOrders()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalSalesOrdersQuery(customerId));
        return PortalListResult(result);
    }

    [HttpGet("me/quotes")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public async Task<ActionResult<List<PortalQuoteListItem>>> Quotes()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalQuotesQuery(customerId));
        return PortalListResult(result);
    }

    [HttpGet("me/invoices")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public async Task<ActionResult<List<PortalInvoiceListItem>>> Invoices()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalInvoicesQuery(customerId));
        return PortalListResult(result);
    }

    [HttpGet("me/shipments")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public async Task<ActionResult<List<PortalShipmentListItem>>> Shipments()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalShipmentsQuery(customerId));
        return PortalListResult(result);
    }

    [HttpPost("me/quotes/{id:int}/respond")]
    [Authorize(AuthenticationSchemes = PortalTokenService.SchemeName)]
    public async Task<IActionResult> RespondToQuote(int id, [FromBody] QuoteResponseRequest request)
    {
        var (customerId, contactId) = RequirePortalClaims();
        await mediator.Send(new RespondToQuoteCommand(id, customerId, contactId, request.Accepted));
        return NoContent();
    }

    private ActionResult<List<T>> PortalListResult<T>(PortalList<T> list)
    {
        Response.Headers[PortalReadRules.TotalCountHeader] = list.TotalCount.ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (list.ExternalAccounting)
            Response.Headers[PortalReadRules.ExternalAccountingHeader] = "true";
        return Ok(list.Items);
    }

    private (int CustomerId, int ContactId) RequirePortalClaims()
    {
        var portalSession = User.FindFirst(PortalTokenService.SessionClaim)?.Value;
        if (portalSession != "true")
            throw new UnauthorizedAccessException("This endpoint requires a portal session token.");

        var customerIdRaw = User.FindFirst(PortalTokenService.CustomerIdClaim)?.Value;
        var contactIdRaw = User.FindFirst(PortalTokenService.ContactIdClaim)?.Value;

        if (!int.TryParse(customerIdRaw, out var customerId) ||
            !int.TryParse(contactIdRaw, out var contactId))
        {
            throw new UnauthorizedAccessException("Portal session token is missing required claims.");
        }

        return (customerId, contactId);
    }

    private PortalIdentityModel ResolvePortalIdentity()
    {
        var (customerId, contactId) = RequirePortalClaims();
        return new PortalIdentityModel(
            ContactId: contactId,
            CustomerId: customerId,
            CustomerName: string.Empty, // populated by frontend from /dashboard if needed
            ContactFirstName: User.FindFirst(ClaimTypes.GivenName)?.Value ?? string.Empty,
            ContactLastName: User.FindFirst(ClaimTypes.Surname)?.Value ?? string.Empty,
            ContactEmail: User.FindFirst(ClaimTypes.Email)?.Value);
    }
}

public record RequestMagicLinkRequest(string Email);
public record ExchangeRequest(string Token);
public record QuoteResponseRequest(bool Accepted);
