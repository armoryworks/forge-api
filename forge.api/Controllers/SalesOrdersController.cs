using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Authorization;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.SalesOrders;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Core.Models.Communications;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/orders")]
[Authorize(Roles = SalesOrderRoles.Read)]
[RequiresCapability("CAP-O2C-SO")]
public class SalesOrdersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SalesOrderListItemModel>>> GetSalesOrders(
        [FromQuery] int? customerId,
        [FromQuery] SalesOrderStatus? status)
    {
        var result = await mediator.Send(new GetSalesOrdersQuery(customerId, status));
        return Ok(result);
    }

    /// <summary>
    /// What authorized this order — the document, its hash, when it arrived and
    /// from whom, with routes through to the original message and the agreement
    /// chain behind it. Null when the order was keyed in or converted from a
    /// quote, which is information rather than an error.
    /// </summary>
    [HttpGet("{id:int}/authorization")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<SalesOrderAuthorizationResponseModel?>> GetAuthorization(
        int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderAuthorizationQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> GetSalesOrder(int id)
    {
        var result = await mediator.Send(new GetSalesOrderByIdQuery(id));
        Response.Headers.ETag = $"\"{result.RowVersion}\"";
        return Ok(result);
    }

    /// <summary>
    /// #27 — sales-order lines available to associate with a new job. Defaults to lines
    /// not actively assigned to an open job; <c>includeAssigned=true</c> surfaces the rest.
    /// </summary>
    [HttpGet("assignable-lines")]
    public async Task<ActionResult<List<AssignableSalesOrderLineModel>>> GetAssignableLines(
        [FromQuery] bool includeAssigned = false,
        [FromQuery] string? search = null,
        CancellationToken cancellationToken = default)
    {
        var result = await mediator.Send(
            new GetAssignableSalesOrderLinesQuery(includeAssigned, search), cancellationToken);
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<SalesOrderListItemModel>> CreateSalesOrder(CreateSalesOrderRequestModel request)
    {
        var result = await mediator.Send(new CreateSalesOrderCommand(
            request.CustomerId, request.QuoteId, request.ShippingAddressId,
            request.BillingAddressId, request.CreditTerms, request.RequestedDeliveryDate,
            request.CustomerPO, request.Notes, request.TaxRate, request.Lines, request.OrderNumber,
            request.CurrencyId, request.FxRate, request.FreightTerms, request.PaymentTermsId,
            request.TaxOverrideReason, request.TaxOverrideNote));
        return CreatedAtAction(nameof(GetSalesOrder), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    [IfMatch(typeof(SalesOrder))]
    public async Task<IActionResult> UpdateSalesOrder(int id, UpdateSalesOrderRequestModel request)
    {
        await mediator.Send(new UpdateSalesOrderCommand(
            id, request.ShippingAddressId, request.BillingAddressId,
            request.CreditTerms, request.RequestedDeliveryDate,
            request.CustomerPO, request.Notes, request.TaxRate, request.OrderNumber,
            request.CurrencyId, request.FxRate, request.FreightTerms, request.ClearFreightTerms, request.PaymentTermsId,
            request.TaxOverrideReason, request.TaxOverrideNote));
        return NoContent();
    }

    [HttpPost("{id:int}/lines")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> AddSalesOrderLine(int id, CreateSalesOrderLineModel request)
    {
        var result = await mediator.Send(new AddSalesOrderLineCommand(id, request));
        return Ok(result);
    }

    [HttpPut("{id:int}/lines/{lineId:int}")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> UpdateSalesOrderLine(
        int id, int lineId, UpdateOrderLineRequestModel request)
    {
        var result = await mediator.Send(new UpdateSalesOrderLineCommand(id, lineId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<SalesOrderDetailResponseModel>> DeleteSalesOrderLine(int id, int lineId)
    {
        var result = await mediator.Send(new DeleteSalesOrderLineCommand(id, lineId));
        return Ok(result);
    }

    [HttpPost("{id:int}/confirm")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<ConfirmSalesOrderResponseModel>> ConfirmSalesOrder(int id)
    {
        var result = await mediator.Send(new ConfirmSalesOrderCommand(id));
        return Ok(result);
    }

    [HttpPost("{id:int}/create-missing-jobs")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    [RequiresCapability("CAP-MFG-WO-RELEASE")]
    public async Task<ActionResult<CreateJobsForSalesOrderLinesResponseModel>> CreateMissingJobs(
        int id, [FromQuery] int? lineId, CancellationToken ct)
    {
        var lineIds = lineId is int single ? new[] { single } : null;
        var result = await mediator.Send(new CreateJobsForSalesOrderLinesCommand(id, lineIds), ct);
        return Ok(result);
    }

    // ─── Customer acceptance (production gate; behavior gated by CAP-O2C-SO-ACCEPTANCE) ───

    [HttpGet("{id:int}/acceptance")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<List<Forge.Api.Features.SalesOrders.Acceptance.SalesOrderAcceptanceResponseModel>>> GetAcceptances(int id)
    {
        var result = await mediator.Send(new Forge.Api.Features.SalesOrders.Acceptance.GetSalesOrderAcceptancesQuery(id));
        return Ok(result);
    }

    /// <summary>Record an offline customer acceptance (upload / fax / email / verbal). File tagged CustomerAcceptance.</summary>
    [HttpPost("{id:int}/acceptance")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    [RequestSizeLimit(52_428_800)]
    public async Task<ActionResult<Forge.Api.Features.SalesOrders.Acceptance.SalesOrderAcceptanceResponseModel>> RecordAcceptance(
        int id, [FromForm] AcceptanceMethod method, [FromForm] string? note, IFormFile? file)
    {
        var result = await mediator.Send(
            new Forge.Api.Features.SalesOrders.Acceptance.RecordManualAcceptanceCommand(id, method, note, file));
        return Ok(result);
    }

    [HttpDelete("{id:int}/acceptance/{acceptanceId:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> RevokeAcceptance(int id, int acceptanceId, [FromQuery] string? reason)
    {
        await mediator.Send(new Forge.Api.Features.SalesOrders.Acceptance.RevokeSalesOrderAcceptanceCommand(id, acceptanceId, reason));
        return NoContent();
    }

    /// <summary>E-signature channel — send the order to the customer to sign via the signing provider.</summary>
    [HttpPost("{id:int}/acceptance/send-signature")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<Forge.Api.Features.SalesOrders.Acceptance.SendForSignatureResponseModel>> SendForSignature(
        int id, [FromBody] SendForSignatureRequest request)
    {
        var result = await mediator.Send(
            new Forge.Api.Features.SalesOrders.Acceptance.SendSalesOrderForSignatureCommand(id, request.SignerEmail, request.SignerName));
        return Ok(result);
    }

    /// <summary>Reconcile a pending e-signature with the provider (poll → store signed PDF → Accepted).</summary>
    [HttpPost("{id:int}/acceptance/{acceptanceId:int}/check-signature")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<Forge.Api.Features.SalesOrders.Acceptance.SalesOrderAcceptanceResponseModel>> CheckSignature(
        int id, int acceptanceId)
    {
        var result = await mediator.Send(
            new Forge.Api.Features.SalesOrders.Acceptance.CompleteSignatureAcceptanceCommand(id, acceptanceId));
        return Ok(result);
    }

    /// <summary>Public accept portal (staff side) — mint a token + second-key link for the customer to accept online.</summary>
    [HttpPost("{id:int}/acceptance/request-portal")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<Forge.Api.Features.SalesOrders.Acceptance.RequestPublicAcceptanceResponseModel>> RequestPortal(
        int id, [FromBody] RequestPortalRequest request)
    {
        var result = await mediator.Send(new Forge.Api.Features.SalesOrders.Acceptance.RequestPublicAcceptanceCommand(
            id, request.RecipientEmail, request.VerificationKey, request.ValidDays ?? 14, request.Language));
        return Ok(result);
    }

    /// <summary>Email-ingest seam — register an inbound acceptance email as a Pending record for staff review.</summary>
    [HttpPost("{id:int}/acceptance/email-ingest")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<Forge.Api.Features.SalesOrders.Acceptance.SalesOrderAcceptanceResponseModel>> IngestEmailAcceptance(
        int id, [FromBody] EmailIngestRequest request)
    {
        var result = await mediator.Send(
            new Forge.Api.Features.SalesOrders.Acceptance.IngestEmailAcceptanceCommand(id, request.FromEmail, request.Note));
        return Ok(result);
    }

    /// <summary>Confirm a pending inbound-email acceptance (staff review → Accepted).</summary>
    [HttpPost("{id:int}/acceptance/{acceptanceId:int}/confirm-email")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<Forge.Api.Features.SalesOrders.Acceptance.SalesOrderAcceptanceResponseModel>> ConfirmEmailAcceptance(
        int id, int acceptanceId)
    {
        var result = await mediator.Send(
            new Forge.Api.Features.SalesOrders.Acceptance.ConfirmEmailAcceptanceCommand(id, acceptanceId));
        return Ok(result);
    }

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<IActionResult> CancelSalesOrder(int id, [FromBody] CancelSalesOrderRequestModel? request = null)
    {
        await mediator.Send(new CancelSalesOrderCommand(id, request?.FeeAmount, request?.FeeReason));
        return NoContent();
    }

    /// <summary>Post-lock change control — see CreateAddendumOrder.</summary>
    [HttpPost("{id:int}/addendum")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<SalesOrderListItemModel>> CreateAddendum(int id)
    {
        var result = await mediator.Send(new CreateAddendumOrderCommand(id));
        return CreatedAtAction(nameof(GetSalesOrder), new { id = result.Id }, result);
    }

    // One-click: ship the order's production-complete, unshipped lines (creates the shipment for you).
    [HttpPost("{id:int}/create-shipment")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<ShipmentListItemModel>> CreateShipmentFromOrder(int id)
    {
        var result = await mediator.Send(new Forge.Api.Features.Shipments.CreateShipmentFromSalesOrderCommand(id));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    [IfMatch(typeof(SalesOrder))]
    public async Task<IActionResult> DeleteSalesOrder(int id)
    {
        await mediator.Send(new DeleteSalesOrderCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/schedule")]
    public async Task<ActionResult<List<ScheduleMilestoneModel>>> GetSchedule(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderScheduleQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/documents")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<List<FileAttachmentResponseModel>>> GetDocuments(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderDocumentsQuery(id), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}/invoices")]
    [Authorize(Roles = SalesOrderRoles.Edit)]
    public async Task<ActionResult<List<SalesOrderInvoiceModel>>> GetInvoices(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetSalesOrderInvoicesQuery(id), ct);
        return Ok(result);
    }
}

/// <summary>Body for sending a Sales Order to the customer for e-signature.</summary>
public record SendForSignatureRequest(string SignerEmail, string SignerName);

/// <summary>Body for minting a public accept-portal link.</summary>
public record RequestPortalRequest(string RecipientEmail, string VerificationKey, int? ValidDays, string? Language = null);

/// <summary>Body for registering an inbound acceptance email.</summary>
public record EmailIngestRequest(string FromEmail, string? Note);
