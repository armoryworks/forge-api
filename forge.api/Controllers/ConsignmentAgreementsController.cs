using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Inventory;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/consignment-agreements")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer")]
[RequiresCapability("CAP-MD-CONTRACTS-CONSIGNMENT")]
public class ConsignmentAgreementsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<ConsignmentAgreementResponseModel>>> GetAgreements(
        [FromQuery] int? vendorId,
        [FromQuery] int? customerId,
        [FromQuery] ConsignmentAgreementStatus? status,
        [FromQuery] int? partId)
    {
        var result = await mediator.Send(new GetConsignmentAgreementsQuery(vendorId, customerId, status, partId));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ConsignmentAgreementResponseModel>> GetAgreement(int id)
    {
        var result = await mediator.Send(new GetConsignmentAgreementQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<ConsignmentAgreementResponseModel>> CreateAgreement([FromBody] CreateConsignmentAgreementRequestModel request)
    {
        var result = await mediator.Send(new CreateConsignmentAgreementCommand(request));
        return Created($"/api/v1/consignment-agreements/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ConsignmentAgreementResponseModel>> UpdateAgreement(int id, [FromBody] UpdateConsignmentAgreementRequestModel request)
    {
        var result = await mediator.Send(new UpdateConsignmentAgreementCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/consume")]
    public async Task<ActionResult<ConsignmentTransactionResponseModel>> RecordConsumption(int id, [FromBody] RecordConsignmentTransactionRequestModel request)
    {
        var result = await mediator.Send(new RecordConsignmentConsumptionCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/receive")]
    public async Task<ActionResult<ConsignmentTransactionResponseModel>> RecordReceipt(int id, [FromBody] RecordConsignmentTransactionRequestModel request)
    {
        var result = await mediator.Send(new RecordConsignmentReceiptCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/reconcile")]
    public async Task<ActionResult<ConsignmentReconciliationResponseModel>> Reconcile(int id, [FromBody] ReconcileConsignmentRequestModel request)
    {
        var result = await mediator.Send(new ReconcileConsignmentCommand(id, request));
        return Ok(result);
    }

    [HttpGet("stock-summary")]
    public async Task<ActionResult<ConsignmentStockSummaryResponseModel>> GetStockSummary(
        [FromQuery] int? vendorId,
        [FromQuery] int? customerId)
    {
        var result = await mediator.Send(new GetConsignmentStockSummaryQuery(vendorId, customerId));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteAgreement(int id)
    {
        await mediator.Send(new DeleteConsignmentAgreementCommand(id));
        return NoContent();
    }
}
