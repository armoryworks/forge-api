using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.CreditMemos;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — credit memos: drafts, issue, application to invoices, refunds and void. Standalone memos
/// need CAP-O2C-CREDITMEMO; memos a return issued (CAP-O2C-RMA) can still be applied, refunded and voided.
/// </summary>
[ApiController]
[Route("api/v1/credit-memos")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresAnyCapability("CAP-O2C-CREDITMEMO", "CAP-O2C-RMA")]
public class CreditMemosController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    [RequiresCapability("CAP-O2C-CREDITMEMO")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> CreateCreditMemo(
        CreateCreditMemoRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateCreditMemoCommand(request), ct);
        return CreatedAtAction(nameof(CreditMemoReadsController.GetCreditMemo), "CreditMemoReads", new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(CreditMemo))]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> UpdateCreditMemo(
        int id, UpdateCreditMemoRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateCreditMemoCommand(id, request), ct));

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(CreditMemo))]
    public async Task<IActionResult> DeleteCreditMemo(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteCreditMemoCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/issue")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> IssueCreditMemo(int id, CancellationToken ct)
        => Ok(await mediator.Send(new IssueCreditMemoCommand(id), ct));

    [HttpPost("{id:int}/applications")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> ApplyCreditMemo(
        int id, ApplyCreditMemoRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new ApplyCreditMemoCommand(id, request.InvoiceId, request.Amount), ct));

    [HttpDelete("{id:int}/applications/{applicationId:int}")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> UnapplyCreditMemo(int id, int applicationId, CancellationToken ct)
        => Ok(await mediator.Send(new UnapplyCreditMemoCommand(id, applicationId), ct));

    [HttpPost("{id:int}/refund")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> RefundCreditMemo(
        int id, CustomerRefundRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new RefundCreditMemoCommand(id, request), ct));

    [HttpPost("{id:int}/void")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<CreditMemoDetailResponseModel>> VoidCreditMemo(
        int id, VoidCreditMemoRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new VoidCreditMemoCommand(id, request.Reason), ct));

    [HttpPost("refunds/{refundId:int}/void")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> VoidRefund(int refundId, VoidCreditMemoRequestModel request, CancellationToken ct)
    {
        await mediator.Send(new VoidCustomerRefundCommand(refundId, request.Reason), ct);
        return NoContent();
    }
}
