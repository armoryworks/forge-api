using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.VendorCredits;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — vendor credit memo list, detail, PDF and the vendor statement. Procurement reads them
/// against its vendors and the Controller reviews payables; neither creates, issues, applies or voids a memo.
/// </summary>
[ApiController]
[Route("api/v1/vendor-credit-memos")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Procurement,Controller")]
[RequiresCapability("CAP-P2P-BILL")]
public class VendorCreditMemoReadsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<VendorCreditMemoListItemModel>>> GetVendorCreditMemos(
        [FromQuery] int? vendorId,
        [FromQuery] VendorCreditMemoStatus? status,
        [FromQuery] int? vendorBillId,
        [FromQuery] bool openOnly,
        [FromQuery] string? q,
        [FromQuery] int? currencyId,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorCreditMemosQuery(vendorId, status, vendorBillId, openOnly, q, currencyId), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> GetVendorCreditMemo(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorCreditMemoByIdQuery(id), ct));

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetVendorCreditMemoPdf(int id, [FromQuery] string? lang, CancellationToken ct)
    {
        var pdf = await mediator.Send(new GenerateVendorCreditMemoPdfQuery(id, lang), ct);
        return File(pdf, "application/pdf", $"vendor-credit-memo-{id}.pdf");
    }

    /// <summary>The vendor's open bills and open credits, the credits negative, netted per currency.</summary>
    [HttpGet("statement/{vendorId:int}")]
    public async Task<ActionResult<VendorStatementModel>> GetVendorStatement(int vendorId, CancellationToken ct)
        => Ok(await mediator.Send(new GetVendorStatementQuery(vendorId), ct));
}
