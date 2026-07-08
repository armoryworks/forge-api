using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.CustomerPoDocuments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// S4a — internal customer-PO document for a sales order. The record is a
/// thin identity row; GET responses render live from the current SO.
/// Capability reuses the sales-orders gate (CAP-O2C-SO) — the document is a
/// projection of the SO aggregate, not its own capability surface.
/// </summary>
[ApiController]
[Route("api/v1/orders/{salesOrderId:int}/customer-po")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-O2C-SO")]
public class CustomerPoDocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<CustomerPoDocumentResponseModel>> GetCustomerPoDocument(int salesOrderId)
    {
        var result = await mediator.Send(new GetCustomerPoDocumentQuery(salesOrderId));
        return Ok(result);
    }

    [HttpGet("pdf")]
    public async Task<IActionResult> GetCustomerPoDocumentPdf(int salesOrderId)
    {
        var pdf = await mediator.Send(new GetCustomerPoDocumentPdfQuery(salesOrderId));
        return File(pdf, "application/pdf", $"customer-po-{salesOrderId}.pdf");
    }

    /// <summary>Manual generation — used when sales:auto_customer_po_enabled is off.</summary>
    [HttpPost]
    public async Task<ActionResult<CustomerPoDocumentSummaryModel>> GenerateCustomerPoDocument(int salesOrderId)
    {
        var result = await mediator.Send(new GenerateCustomerPoDocumentCommand(salesOrderId));
        return CreatedAtAction(nameof(GetCustomerPoDocument), new { salesOrderId }, result);
    }
}
