using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.Quotes;
using Forge.Api.Features.Terms;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/quotes")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-QUOTE")]
public class QuotesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<QuoteListItemModel>>> GetQuotes(
        [FromQuery] int? customerId,
        [FromQuery] QuoteStatus? status)
    {
        var result = await mediator.Send(new GetQuotesQuery(customerId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<QuoteDetailResponseModel>> GetQuote(int id)
    {
        var result = await mediator.Send(new GetQuoteByIdQuery(id));
        return Ok(result);
    }

    /// <summary>
    /// AUDIT-19-S1: the customer-specific price-list unit price for a part (null if none). Line
    /// dialogs call this on part-select to pre-fill the price (forge-ui#26).
    /// </summary>
    [HttpGet("resolve-price")]
    public async Task<ActionResult<decimal?>> ResolvePrice(
        [FromQuery] int customerId, [FromQuery] int partId, CancellationToken ct)
        => Ok(await mediator.Send(new ResolvePartPriceQuery(customerId, partId), ct));

    [HttpPost]
    public async Task<ActionResult<QuoteListItemModel>> CreateQuote(CreateQuoteRequestModel request)
    {
        var result = await mediator.Send(new CreateQuoteCommand(
            request.CustomerId, request.ShippingAddressId, request.ExpirationDate,
            request.Notes, request.TaxRate, request.Lines, request.CustomerPO));
        return CreatedAtAction(nameof(GetQuote), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(Quote))]
    public async Task<IActionResult> UpdateQuote(int id, UpdateQuoteRequestModel request)
    {
        await mediator.Send(new UpdateQuoteCommand(
            id, request.ShippingAddressId, request.ExpirationDate,
            request.Notes, request.TaxRate, request.CustomerPO));
        return NoContent();
    }

    [HttpPost("{id:int}/lines")]
    public async Task<ActionResult<QuoteDetailResponseModel>> AddQuoteLine(int id, CreateQuoteLineModel request)
    {
        var result = await mediator.Send(new AddQuoteLineCommand(id, request));
        return Ok(result);
    }

    [HttpPut("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<QuoteDetailResponseModel>> UpdateQuoteLine(
        int id, int lineId, UpdateOrderLineRequestModel request)
    {
        var result = await mediator.Send(new UpdateQuoteLineCommand(id, lineId, request));
        return Ok(result);
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<QuoteDetailResponseModel>> DeleteQuoteLine(int id, int lineId)
    {
        var result = await mediator.Send(new DeleteQuoteLineCommand(id, lineId));
        return Ok(result);
    }

    [HttpPost("{id:int}/send")]
    public async Task<IActionResult> SendQuote(int id)
    {
        await mediator.Send(new SendQuoteCommand(id));
        return NoContent();
    }

    /// <summary>
    /// S3 — compiled T&amp;C sections (company + customer + line parts) for the
    /// send-quote dialog's preview pane. Read-only; the immutable snapshot is
    /// only taken on send.
    /// </summary>
    [HttpGet("{id:int}/terms/preview")]
    public async Task<ActionResult<CompiledTermsResult>> PreviewQuoteTerms(int id)
    {
        var result = await mediator.Send(new PreviewQuoteTermsQuery(id));
        return Ok(result);
    }

    /// <summary>
    /// S3 — email the quote (PDF + compiled terms + public full-terms link) and
    /// flip it to Sent in one call. The public base URL for the terms link is
    /// derived from this request (same pattern as the customer-portal magic link).
    /// </summary>
    [HttpPost("{id:int}/send-email")]
    public async Task<IActionResult> SendQuoteEmail(int id, SendQuoteEmailRequestModel request)
    {
        var publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
        await mediator.Send(new SendQuoteEmailCommand(id, request.RecipientEmail, request.Message, publicBaseUrl));
        return NoContent();
    }

    [HttpPost("{id:int}/accept")]
    public async Task<IActionResult> AcceptQuote(int id)
    {
        await mediator.Send(new AcceptQuoteCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/reject")]
    public async Task<IActionResult> RejectQuote(int id)
    {
        await mediator.Send(new RejectQuoteCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/convert")]
    public async Task<ActionResult<SalesOrderListItemModel>> ConvertToOrder(int id)
    {
        var result = await mediator.Send(new ConvertQuoteToOrderCommand(id));
        return CreatedAtAction(
            actionName: "GetSalesOrder",
            controllerName: "SalesOrders",
            routeValues: new { id = result.Id },
            value: result);
    }

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(Quote))]
    public async Task<IActionResult> DeleteQuote(int id)
    {
        await mediator.Send(new DeleteQuoteCommand(id));
        return NoContent();
    }

    // S4a — sales-side settings (auto customer-PO toggle). Reads stay at the
    // controller's role set; the mutation is Admin-only via the action-level
    // attribute (ANDed with the controller attribute — same tightening pattern
    // as AutoPoController's settings endpoints).
    [HttpGet("settings")]
    public async Task<ActionResult<QuoteSettingsResponseModel>> GetSettings()
    {
        var result = await mediator.Send(new GetQuoteSettingsQuery());
        return Ok(result);
    }

    [HttpPut("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<QuoteSettingsResponseModel>> UpdateSettings(
        [FromBody] UpdateQuoteSettingsRequestModel model)
    {
        var result = await mediator.Send(new UpdateQuoteSettingsCommand(model.AutoCustomerPoEnabled));
        return Ok(result);
    }
}
