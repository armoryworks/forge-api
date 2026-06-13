using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.Invoices;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — Standalone mode: full CRUD. Integrated mode: read-only.
/// </summary>
[ApiController]
[Route("api/v1/invoices")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-INVOICE")]
public class InvoicesController(IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Phase 3 F7-broad / WU-22 — standardised paged-list contract.
    ///
    /// New shape:
    ///   <c>GET /invoices?page=1&amp;pageSize=25&amp;sort=invoiceDate&amp;order=desc&amp;q=INV-001&amp;customerId=4&amp;status=Sent&amp;dateFrom=2025-01-01&amp;dateTo=2025-12-31</c>
    ///
    /// Response: <c>{ items, totalCount, page, pageSize }</c>.
    ///
    /// Backward compat: the legacy <c>?customerId=&amp;status=</c> form
    /// continues to work. Existing UI callers that don't pass any query
    /// params get the standard default (page 1, 25 records, createdAt desc).
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<PagedResponse<InvoiceListItemModel>>> GetInvoices(
        [FromQuery] InvoiceListQuery query,
        CancellationToken ct)
    {
        var result = await mediator.Send(new GetInvoicesQuery(query), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<InvoiceDetailResponseModel>> GetInvoice(int id)
    {
        var result = await mediator.Send(new GetInvoiceByIdQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceListItemModel>> CreateInvoice(CreateInvoiceRequestModel request)
    {
        var result = await mediator.Send(new CreateInvoiceCommand(
            request.CustomerId, request.SalesOrderId, request.ShipmentId,
            request.InvoiceDate, request.DueDate, request.CreditTerms,
            request.TaxRate, request.Notes, request.Lines,
            CurrencyId: request.CurrencyId, FxRate: request.FxRate));
        return CreatedAtAction(nameof(GetInvoice), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> SendInvoice(int id)
    {
        await mediator.Send(new SendInvoiceCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/email")]
    public async Task<IActionResult> EmailInvoice(int id, SendInvoiceEmailRequestModel request)
    {
        await mediator.Send(new SendInvoiceEmailCommand(id, request.RecipientEmail));
        return NoContent();
    }

    [HttpPost("{id:int}/void")]
    public async Task<IActionResult> VoidInvoice(int id)
    {
        await mediator.Send(new VoidInvoiceCommand(id));
        return NoContent();
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetInvoicePdf(int id)
    {
        var pdf = await mediator.Send(new GenerateInvoicePdfQuery(id));
        return File(pdf, "application/pdf", $"invoice-{id}.pdf");
    }

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(Invoice))]
    public async Task<IActionResult> DeleteInvoice(int id)
    {
        await mediator.Send(new DeleteInvoiceCommand(id));
        return NoContent();
    }

    [HttpGet("uninvoiced-jobs")]
    public async Task<ActionResult<List<UninvoicedJobResponseModel>>> GetUninvoicedJobs()
    {
        var result = await mediator.Send(new GetUninvoicedJobsQuery());
        return Ok(result);
    }

    [HttpPost("from-job/{jobId:int}")]
    public async Task<ActionResult<InvoiceListItemModel>> CreateInvoiceFromJob(int jobId)
    {
        var result = await mediator.Send(new CreateInvoiceFromJobCommand(jobId));
        return CreatedAtAction(nameof(GetInvoice), new { id = result.Id }, result);
    }

    [HttpGet("queue-settings")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<InvoiceQueueSettingsResponse>> GetQueueSettings()
    {
        var result = await mediator.Send(new GetInvoiceQueueSettingsQuery());
        return Ok(result);
    }

    [HttpPut("queue-settings")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> UpdateQueueSettings(UpdateInvoiceQueueSettingsCommand command)
    {
        await mediator.Send(command);
        return NoContent();
    }
}
