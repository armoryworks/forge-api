using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorReturns;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Returns to vendor (RTV) that NCR Return To Vendor dispositions open: list, detail, ship, record the vendor's credit
/// and the packing document. The receiving roles of <see cref="PurchaseOrdersController"/> ship and credit them.
/// </summary>
[ApiController]
[Route("api/v1/vendor-returns")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Procurement")]
[RequiresCapability("CAP-P2P-PO")]
[RequiresAnyCapability("CAP-QC-NCR")]
public class VendorReturnsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorReturnListItemModel>>> GetVendorReturns(
        [FromQuery] int? vendorId, [FromQuery] int? nonConformanceId, [FromQuery] VendorReturnStatus? status, CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorReturnsQuery(vendorId, nonConformanceId, status), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorReturnDetailResponseModel>> GetVendorReturn(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorReturnByIdQuery(id), ct));

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetVendorReturnPdf(int id, [FromQuery] string? lang, CancellationToken ct)
    {
        var pdf = await mediator.Send(new GenerateVendorReturnPdfQuery(id, lang), ct);
        return File(pdf, "application/pdf", $"return-to-vendor-{id}.pdf");
    }

    [HttpPost("{id:int}/ship")]
    public async Task<ActionResult<VendorReturnDetailResponseModel>> ShipVendorReturn(
        int id, ShipVendorReturnRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new ShipVendorReturnCommand(id, request), ct));

    /// <summary>
    /// Issues the vendor's credit memo for a shipped return, clearing the vendor returns clearing account. Needs
    /// CAP-P2P-BILL too (checked on the command).
    /// </summary>
    [HttpPost("{id:int}/credit")]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> RecordVendorReturnCredit(
        int id, RecordVendorReturnCreditRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new RecordVendorReturnCreditCommand(id, request), ct));
}
