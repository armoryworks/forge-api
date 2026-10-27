using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.CreditMemos;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — credit memo list, detail and PDF. The Controller reviews receivables here but does not
/// issue, apply, refund or void; those live on <see cref="CreditMemosController"/>, whose role list is ANDed with
/// any action-level list.
/// </summary>
[ApiController]
[Route("api/v1/credit-memos")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Controller")]
[RequiresAnyCapability("CAP-O2C-CREDITMEMO", "CAP-O2C-RMA")]
public class CreditMemoReadsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CreditMemoListItemModel>>> GetCreditMemos(
        [FromQuery] int? customerId,
        [FromQuery] CreditMemoStatus? status,
        [FromQuery] int? invoiceId,
        [FromQuery] bool openOnly,
        [FromQuery] string? q,
        CancellationToken ct)
        => Ok(await mediator.Send(new GetCreditMemosQuery(customerId, status, invoiceId, openOnly, q), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> GetCreditMemo(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetCreditMemoByIdQuery(id), ct));

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> GetCreditMemoPdf(int id, [FromQuery] string? language, CancellationToken ct)
    {
        var pdf = await mediator.Send(new GenerateCreditMemoPdfQuery(id, language), ct);
        return File(pdf, "application/pdf", $"credit-memo-{id}.pdf");
    }
}
