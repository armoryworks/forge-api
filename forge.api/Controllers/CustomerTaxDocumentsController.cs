using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.CustomerTaxDocuments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// S1 — customer state tax certificates (resale/exemption documents) with a
/// verification workflow. A Verified, unexpired document is what unlocks
/// editing a quote's tax rate (see <see cref="Forge.Api.Services.TaxOverrideGuard"/>).
/// The read-only tax-editability probe lives on <see cref="CustomersController"/>
/// so every quote-dialog role can call it.
/// </summary>
[ApiController]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-MD-CUSTOMERS")]
public class CustomerTaxDocumentsController(IMediator mediator) : ControllerBase
{
    [HttpGet("api/v1/customers/{customerId:int}/tax-documents")]
    public async Task<ActionResult<List<CustomerTaxDocumentResponseModel>>> GetForCustomer(int customerId)
    {
        var result = await mediator.Send(new GetCustomerTaxDocumentsQuery(customerId));
        return Ok(result);
    }

    [HttpPost("api/v1/customers/{customerId:int}/tax-documents")]
    public async Task<ActionResult<CustomerTaxDocumentResponseModel>> Create(
        int customerId, [FromBody] CreateCustomerTaxDocumentRequestModel request)
    {
        var result = await mediator.Send(new CreateCustomerTaxDocumentCommand(
            customerId, request.FileAttachmentId, request.StateCode,
            request.CertificateType, request.CertificateNumber, request.ExpirationDate));
        return Created($"/api/v1/customer-tax-documents/{result.Id}", result);
    }

    [HttpPost("api/v1/customer-tax-documents/{id:int}/verify")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Verify(int id)
    {
        await mediator.Send(new VerifyCustomerTaxDocumentCommand(id));
        return NoContent();
    }

    [HttpPost("api/v1/customer-tax-documents/{id:int}/reject")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Reject(int id, [FromBody] RejectCustomerTaxDocumentRequestModel request)
    {
        await mediator.Send(new RejectCustomerTaxDocumentCommand(id, request.Reason));
        return NoContent();
    }

    [HttpDelete("api/v1/customer-tax-documents/{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteCustomerTaxDocumentCommand(id));
        return NoContent();
    }
}
