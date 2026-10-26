using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Concurrency;
using Forge.Api.Features.VendorCredits;
using Forge.Core.Entities;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — vendor credit and debit memos (AP): drafts, issue, application to bills and void. The AP
/// twin of <see cref="CreditMemosController"/>, with the roles of <see cref="VendorBillsController"/>. Reads live on
/// <see cref="VendorCreditMemoReadsController"/>, because this controller's role list is ANDed with any action-level
/// list.
/// </summary>
[ApiController]
[Route("api/v1/vendor-credit-memos")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-P2P-BILL")]
public class VendorCreditMemosController(IMediator mediator) : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> CreateVendorCreditMemo(
        CreateVendorCreditMemoRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateVendorCreditMemoCommand(request), ct);
        return CreatedAtAction(nameof(VendorCreditMemoReadsController.GetVendorCreditMemo), "VendorCreditMemoReads", new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    [IfMatch(typeof(VendorCreditMemo))]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> UpdateVendorCreditMemo(
        int id, UpdateVendorCreditMemoRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new UpdateVendorCreditMemoCommand(id, request), ct));

    [HttpDelete("{id:int}")]
    [IfMatch(typeof(VendorCreditMemo))]
    public async Task<IActionResult> DeleteVendorCreditMemo(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteVendorCreditMemoCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/issue")]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> IssueVendorCreditMemo(int id, CancellationToken ct)
        => Ok(await mediator.Send(new IssueVendorCreditMemoCommand(id), ct));

    [HttpPost("{id:int}/applications")]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> ApplyVendorCreditMemo(
        int id, ApplyVendorCreditMemoRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new ApplyVendorCreditMemoCommand(id, request.VendorBillId, request.Amount), ct));

    [HttpDelete("{id:int}/applications/{applicationId:int}")]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> UnapplyVendorCreditMemo(int id, int applicationId, CancellationToken ct)
        => Ok(await mediator.Send(new UnapplyVendorCreditMemoCommand(id, applicationId), ct));

    [HttpPost("{id:int}/void")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<VendorCreditMemoDetailResponseModel>> VoidVendorCreditMemo(
        int id, VoidCreditMemoRequestModel request, CancellationToken ct)
        => Ok(await mediator.Send(new VoidVendorCreditMemoCommand(id, request.Reason), ct));
}
