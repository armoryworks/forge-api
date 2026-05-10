using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using QBEngineer.Api.Capabilities;
using QBEngineer.Api.Features.CustomerPortal;
using QBEngineer.Core.Models;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Phase 1q — customer-facing self-service portal. The two auth endpoints
/// (`request-link`, `exchange`) are <see cref="AllowAnonymous"/> +
/// <see cref="CapabilityBootstrapAttribute"/>-equivalent — they route
/// through the gate via <see cref="RequiresCapabilityAttribute"/> on the
/// controller, but anonymous portal sign-in must be reachable when the
/// capability is enabled. The /me/* endpoints require an authenticated
/// portal session JWT (`portal_session=true` claim).
/// </summary>
[ApiController]
[Route("api/v1/portal")]
[RequiresCapability("CAP-EXT-CUSTOMER-PORTAL")]
public class CustomerPortalController(IMediator mediator) : ControllerBase
{
    [HttpPost("auth/request-link")]
    [AllowAnonymous]
    public async Task<ActionResult<RequestMagicLinkResult>> RequestLink([FromBody] RequestMagicLinkRequest request)
    {
        // Construct portal base URL from incoming Origin header, falling
        // back to the request's own scheme+host. The frontend's
        // /portal/auth/callback?token=... is what the link points at.
        var origin = Request.Headers.Origin.ToString();
        if (string.IsNullOrWhiteSpace(origin)) origin = $"{Request.Scheme}://{Request.Host}";

        var result = await mediator.Send(new RequestMagicLinkCommand(request.Email, origin));
        return Ok(result);
    }

    [HttpPost("auth/exchange")]
    [AllowAnonymous]
    public async Task<ActionResult<PortalSessionResponseModel>> Exchange([FromBody] ExchangeRequest request)
    {
        var result = await mediator.Send(new ExchangeMagicLinkCommand(request.Token));
        return Ok(result);
    }

    [HttpGet("me")]
    [Authorize]
    public ActionResult<PortalIdentityModel> Me()
    {
        var identity = ResolvePortalIdentity();
        return Ok(identity);
    }

    [HttpGet("me/dashboard")]
    [Authorize]
    public async Task<ActionResult<PortalSummaryResponseModel>> Dashboard()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalDashboardQuery(customerId));
        return Ok(result);
    }

    [HttpGet("me/sales-orders")]
    [Authorize]
    public async Task<ActionResult<List<PortalSalesOrderListItem>>> SalesOrders()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalSalesOrdersQuery(customerId));
        return Ok(result);
    }

    [HttpGet("me/quotes")]
    [Authorize]
    public async Task<ActionResult<List<PortalQuoteListItem>>> Quotes()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalQuotesQuery(customerId));
        return Ok(result);
    }

    [HttpGet("me/invoices")]
    [Authorize]
    public async Task<ActionResult<List<PortalInvoiceListItem>>> Invoices()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalInvoicesQuery(customerId));
        return Ok(result);
    }

    [HttpGet("me/shipments")]
    [Authorize]
    public async Task<ActionResult<List<PortalShipmentListItem>>> Shipments()
    {
        var (customerId, _) = RequirePortalClaims();
        var result = await mediator.Send(new GetPortalShipmentsQuery(customerId));
        return Ok(result);
    }

    [HttpPost("me/quotes/{id:int}/respond")]
    [Authorize]
    public async Task<IActionResult> RespondToQuote(int id, [FromBody] QuoteResponseRequest request)
    {
        var (customerId, contactId) = RequirePortalClaims();
        await mediator.Send(new RespondToQuoteCommand(id, customerId, contactId, request.Accepted));
        return NoContent();
    }

    private (int CustomerId, int ContactId) RequirePortalClaims()
    {
        var portalSession = User.FindFirst("portal_session")?.Value;
        if (portalSession != "true")
            throw new UnauthorizedAccessException("This endpoint requires a portal session token.");

        var customerIdRaw = User.FindFirst("customer_id")?.Value;
        var contactIdRaw = User.FindFirst("contact_id")?.Value;

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
