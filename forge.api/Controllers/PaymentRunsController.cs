using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.PaymentRuns;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// ⚡ ACCOUNTING BOUNDARY — persisted payment runs: pay the bills picked from the payment suggestions as one batch with
/// per-vendor status, deducting open vendor credits; resume or cancel an interrupted run. The roles of
/// <see cref="VendorPaymentsController"/>; the Controller also reads them.
/// </summary>
[ApiController]
[Route("api/v1/payment-runs")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Controller")]
[RequiresCapability("CAP-P2P-PAY")]
public class PaymentRunsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PaymentRunListItemModel>>> GetPaymentRuns([FromQuery] PaymentRunStatus? status, CancellationToken ct)
        => Ok(await mediator.Send(new GetPaymentRunsQuery(status), ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PaymentRunDetailResponseModel>> GetPaymentRun(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetPaymentRunByIdQuery(id), ct));

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<PaymentRunDetailResponseModel>> CreatePaymentRun(CreatePaymentRunRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreatePaymentRunCommand(request), ct);
        return CreatedAtAction(nameof(GetPaymentRun), new { id = result.Id }, result);
    }

    [HttpPost("{id:int}/resume")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<PaymentRunDetailResponseModel>> ResumePaymentRun(int id, CancellationToken ct)
        => Ok(await mediator.Send(new ResumePaymentRunCommand(id), ct));

    [HttpPost("{id:int}/cancel")]
    [Authorize(Roles = "Admin,Manager,OfficeManager")]
    public async Task<ActionResult<PaymentRunDetailResponseModel>> CancelPaymentRun(int id, CancellationToken ct)
        => Ok(await mediator.Send(new CancelPaymentRunCommand(id), ct));
}
