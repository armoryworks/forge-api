using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Api.Features.Customers;
using Forge.Api.Features.OutreachPreferences;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/customers")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM,Engineer")]
[RequiresCapability("CAP-MD-CUSTOMERS")]
public class CustomersController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-partial / WU-17 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /customers?page=1&amp;pageSize=25&amp;sort=createdAt&amp;order=desc&amp;q=acme&amp;isActive=true&amp;dateFrom=2025-01-01&amp;dateTo=2025-12-31&amp;defaultCurrency=USD</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?search=&amp;isActive=</c> form continues
    /// to work — when both <c>q</c> and <c>search</c> are present, <c>q</c>
    /// wins. Existing UI callers that don't pass any query params get the
    /// standard default (page 1, 25 records, createdAt desc).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<CustomerListItemModel>>> GetCustomers(
        [FromQuery] CustomerListQuery query,
        [FromQuery(Name = "search")] string? legacySearch,
        CancellationToken ct)
    {
        // Plumb the legacy `search` param into the standard `q` field if `q`
        // wasn't supplied. Avoids breaking existing UI / harness callers.
        var effective = string.IsNullOrEmpty(query.Q) && !string.IsNullOrEmpty(legacySearch)
            ? query with { Q = legacySearch }
            : query;
        var result = await mediator.Send(new GetCustomerListQuery(effective), ct);
        return Ok(result);
    }

    [HttpGet("dropdown")]
    public async Task<ActionResult<List<CustomerResponseModel>>> GetCustomerDropdown()
    {
        var result = await mediator.Send(new GetCustomersQuery());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CustomerDetailResponseModel>> GetCustomer(int id)
    {
        var result = await mediator.Send(new GetCustomerByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CustomerListItemModel>> CreateCustomer(CreateCustomerRequestModel request)
    {
        // Phase 3 F3 — pass through the new full-record fields. The first four
        // positional args preserve the original signature for compile parity
        // across callers; everything past that is named.
        var result = await mediator.Send(new CreateCustomerCommand(
            request.Name, request.CompanyName, request.Email, request.Phone,
            IsTaxExempt: request.IsTaxExempt,
            TaxExemptionId: request.TaxExemptionId,
            CreditLimit: request.CreditLimit,
            DefaultTaxCodeId: request.DefaultTaxCodeId,
            DefaultCurrency: request.DefaultCurrency,
            BillingAddress: request.BillingAddress,
            ShippingAddress: request.ShippingAddress));
        return CreatedAtAction(nameof(GetCustomer), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCustomer(int id, UpdateCustomerRequestModel request)
    {
        await mediator.Send(new UpdateCustomerCommand(
            id, request.Name, request.CompanyName, request.Email, request.Phone, request.IsActive,
            IsFdaRegulated: request.IsFdaRegulated,
            IsAerospace: request.IsAerospace,
            IsAutomotive: request.IsAutomotive,
            IsItarControlled: request.IsItarControlled,
            IsReferenceOk: request.IsReferenceOk,
            ReferenceNotes: request.ReferenceNotes));
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteCustomer(int id)
    {
        await mediator.Send(new DeleteCustomerCommand(id));
        return NoContent();
    }

    // ─── Contacts ───
    // Wave 5 — multi-contact-per-customer is gated behind CAP-MD-CUSTOMER-CONTACTS
    // for shops that only deal with one buyer per company. Default ON; admins
    // toggle off to hide the Contacts tab + drop the contact CRUD surface.

    [HttpPost("{id:int}/contacts")]
    [RequiresCapability("CAP-MD-CUSTOMER-CONTACTS")]
    public async Task<ActionResult<ContactResponseModel>> CreateContact(int id, CreateContactRequestModel request)
    {
        var result = await mediator.Send(new CreateContactCommand(
            id, request.FirstName, request.LastName, request.Email, request.Phone, request.Role, request.IsPrimary));
        return Created($"/api/v1/customers/{id}/contacts/{result.Id}", result);
    }

    [HttpPut("{id:int}/contacts/{contactId:int}")]
    [RequiresCapability("CAP-MD-CUSTOMER-CONTACTS")]
    public async Task<ActionResult<ContactResponseModel>> UpdateContact(int id, int contactId, UpdateContactRequestModel request)
    {
        var result = await mediator.Send(new UpdateContactCommand(
            id, contactId, request.FirstName, request.LastName, request.Email, request.Phone, request.Role, request.IsPrimary));
        return Ok(result);
    }

    [HttpDelete("{id:int}/contacts/{contactId:int}")]
    [RequiresCapability("CAP-MD-CUSTOMER-CONTACTS")]
    public async Task<IActionResult> DeleteContact(int id, int contactId)
    {
        await mediator.Send(new DeleteContactCommand(id, contactId));
        return NoContent();
    }

    /// <summary>
    /// Phase 1r — flat cross-customer contact listing for
    /// /customers/contacts. Pulls every Contact + parent Customer +
    /// suppression-prefs summary in one query.
    /// </summary>
    [HttpGet("all-contacts")]
    [RequiresCapability("CAP-MD-CUSTOMER-CONTACTS")]
    public async Task<ActionResult<List<FlatContactRowModel>>> GetAllContactsFlat()
        => Ok(await mediator.Send(new GetAllContactsFlatQuery()));

    /// <summary>
    /// Phase 1r — admin oversight of customer-portal access.
    /// GET lists every CustomerPortalAccess row; PUT toggles IsEnabled.
    /// Gated behind the same portal capability the customer-facing side
    /// uses, since "manage who can sign in" is part of the portal feature.
    /// </summary>
    [HttpGet("portal-access")]
    [RequiresCapability("CAP-EXT-CUSTOMER-PORTAL")]
    public async Task<ActionResult<List<PortalAccessRowModel>>> ListPortalAccess()
        => Ok(await mediator.Send(new ListPortalAccessQuery()));

    [HttpPost("portal-access")]
    [RequiresCapability("CAP-EXT-CUSTOMER-PORTAL")]
    public async Task<ActionResult<PortalAccessRowModel>> CreatePortalAccess([FromBody] CreatePortalAccessRequest request)
    {
        var result = await mediator.Send(new CreatePortalAccessCommand(request.ContactId));
        return Created($"/api/v1/customers/portal-access/{result.AccessId}", result);
    }

    [HttpPut("portal-access/{accessId:int}/enabled")]
    [RequiresCapability("CAP-EXT-CUSTOMER-PORTAL")]
    public async Task<IActionResult> SetPortalAccessEnabled(int accessId, [FromBody] SetPortalAccessEnabledRequest request)
    {
        await mediator.Send(new SetPortalAccessEnabledCommand(accessId, request.Enabled));
        return NoContent();
    }

    // ─── Phase 1r — Contact outreach preferences ───
    // 0..1:1 sidecar carrying per-channel opt-outs + cooldown windows.
    // GET returns null when no row exists (default "no opt-outs"); PUT
    // upserts and emits a rolled-up activity-log row indexed at both
    // Contact and Customer (per the activity-logging indexing-points
    // rule).

    [HttpGet("{id:int}/contacts/{contactId:int}/outreach-preferences")]
    [RequiresCapability("CAP-MD-CUSTOMER-CONTACTS")]
    public async Task<ActionResult<OutreachPreferencesResponseModel?>> GetContactOutreachPreferences(int id, int contactId)
    {
        var result = await mediator.Send(new GetContactOutreachPreferencesQuery(contactId));
        return Ok(result);
    }

    [HttpPut("{id:int}/contacts/{contactId:int}/outreach-preferences")]
    [RequiresCapability("CAP-MD-CUSTOMER-CONTACTS")]
    public async Task<ActionResult<OutreachPreferencesResponseModel>> UpdateContactOutreachPreferences(
        int id, int contactId, [FromBody] UpdateOutreachPreferencesRequest request)
    {
        var result = await mediator.Send(new UpdateContactOutreachPreferencesCommand(contactId, request));
        return Ok(result);
    }

    // ─── Contact Interactions ───
    // Wave 5 — interaction logging (call/email/meeting/note timeline) is
    // CAP-MD-CUSTOMER-INTERACTIONS. Default OFF; small shops opt in when
    // they need CRM activity tracking.

    [HttpGet("{id:int}/interactions")]
    [RequiresCapability("CAP-MD-CUSTOMER-INTERACTIONS")]
    public async Task<ActionResult<List<ContactInteractionResponseModel>>> GetInteractions(
        int id, [FromQuery] int? contactId)
    {
        var result = await mediator.Send(new GetContactInteractionsQuery(id, contactId));
        return Ok(result);
    }

    [HttpPost("{id:int}/interactions")]
    [RequiresCapability("CAP-MD-CUSTOMER-INTERACTIONS")]
    public async Task<ActionResult<ContactInteractionResponseModel>> CreateInteraction(
        int id, [FromBody] ContactInteractionRequestModel request)
    {
        var result = await mediator.Send(new CreateContactInteractionCommand(
            id, request.ContactId, request.Type, request.Subject,
            request.Body, request.InteractionDate, request.DurationMinutes));
        return Created($"/api/v1/customers/{id}/interactions/{result.Id}", result);
    }

    [HttpPatch("{id:int}/interactions/{interactionId:int}")]
    [RequiresCapability("CAP-MD-CUSTOMER-INTERACTIONS")]
    public async Task<ActionResult<ContactInteractionResponseModel>> UpdateInteraction(
        int id, int interactionId, [FromBody] ContactInteractionRequestModel request)
    {
        var result = await mediator.Send(new UpdateContactInteractionCommand(
            id, interactionId, request.Type, request.Subject,
            request.Body, request.InteractionDate, request.DurationMinutes));
        return Ok(result);
    }

    [HttpDelete("{id:int}/interactions/{interactionId:int}")]
    [RequiresCapability("CAP-MD-CUSTOMER-INTERACTIONS")]
    public async Task<IActionResult> DeleteInteraction(int id, int interactionId)
    {
        await mediator.Send(new DeleteContactInteractionCommand(id, interactionId));
        return NoContent();
    }

    [HttpGet("{id:int}/activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetCustomerActivity(int id)
    {
        var result = await mediator.Send(new GetEntityActivityQuery("Customer", id));
        return Ok(result);
    }

    [HttpGet("{id:int}/statement")]
    public async Task<IActionResult> GetStatement(int id)
    {
        var pdf = await mediator.Send(new GenerateCustomerStatementQuery(id));
        return File(pdf, "application/pdf", $"statement-{id}.pdf");
    }

    [HttpGet("{id:int}/summary")]
    public async Task<ActionResult<CustomerSummaryResponseModel>> GetSummary(int id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCustomerSummaryQuery(id), ct);
        return Ok(result);
    }

    // ─── Credit Management ───
    // Wave 5 — credit policy + hold workflow gated behind CAP-O2C-CREDIT-LIMITS.
    // Default OFF for COD / prepaid shops where the credit-status card and
    // place/release-hold actions would be noise.

    [HttpGet("{id:int}/credit-status")]
    [RequiresCapability("CAP-O2C-CREDIT-LIMITS")]
    public async Task<ActionResult<CreditStatusResponseModel>> GetCreditStatus(int id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCreditStatusQuery(id), ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/credit-hold")]
    [RequiresCapability("CAP-O2C-CREDIT-LIMITS")]
    public async Task<IActionResult> PlaceCreditHold(int id, [FromBody] PlaceCreditHoldRequestModel request)
    {
        await mediator.Send(new PlaceCreditHoldCommand(id, request.Reason));
        return NoContent();
    }

    [HttpPost("{id:int}/credit-release")]
    [RequiresCapability("CAP-O2C-CREDIT-LIMITS")]
    public async Task<IActionResult> ReleaseCreditHold(int id)
    {
        await mediator.Send(new ReleaseCreditHoldCommand(id));
        return NoContent();
    }

    [HttpGet("credit-risk-report")]
    [RequiresCapability("CAP-O2C-CREDIT-LIMITS")]
    public async Task<ActionResult<List<CreditStatusResponseModel>>> GetCreditRiskReport(CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetCreditRiskReportQuery(), ct);
        return Ok(result);
    }
}

public record SetPortalAccessEnabledRequest(bool Enabled);
public record CreatePortalAccessRequest(int ContactId);
