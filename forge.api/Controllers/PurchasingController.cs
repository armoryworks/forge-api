using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Purchasing;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/purchasing")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-P2P-RFQ")]
public class PurchasingController(IMediator mediator) : ControllerBase
{
    [HttpGet("rfqs")]
    public async Task<ActionResult<List<RfqResponseModel>>> GetRfqs(
        [FromQuery] RfqStatus? status,
        [FromQuery] string? search)
    {
        var result = await mediator.Send(new GetRfqsQuery(status, search));
        return Ok(result);
    }

    [HttpPost("rfqs")]
    public async Task<ActionResult<RfqResponseModel>> CreateRfq(CreateRfqRequestModel request)
    {
        var result = await mediator.Send(new CreateRfqCommand(
            request.PartId,
            request.Quantity,
            request.RequiredDate,
            request.Description,
            request.SpecialInstructions,
            request.ResponseDeadline));
        return CreatedAtAction(nameof(GetRfqById), new { id = result.Id }, result);
    }

    [HttpGet("rfqs/{id:int}")]
    public async Task<ActionResult<RfqDetailResponseModel>> GetRfqById(int id)
    {
        var result = await mediator.Send(new GetRfqByIdQuery(id));
        return Ok(result);
    }

    [HttpPut("rfqs/{id:int}")]
    public async Task<IActionResult> UpdateRfq(int id, CreateRfqRequestModel request)
    {
        await mediator.Send(new UpdateRfqCommand(
            id,
            request.PartId,
            request.Quantity,
            request.RequiredDate,
            request.Description,
            request.SpecialInstructions,
            request.ResponseDeadline,
            null));
        return NoContent();
    }

    [HttpPost("rfqs/{id:int}/send")]
    public async Task<IActionResult> SendRfqToVendors(int id, SendRfqToVendorsRequestModel request)
    {
        await mediator.Send(new SendRfqToVendorsCommand(id, request.VendorIds));
        return NoContent();
    }

    [HttpPost("rfqs/{id:int}/responses")]
    public async Task<IActionResult> RecordVendorResponse(int id, RecordVendorResponseRequestModel request)
    {
        await mediator.Send(new RecordVendorResponseCommand(
            id,
            request.VendorId,
            request.UnitPrice,
            request.LeadTimeDays,
            request.MinimumOrderQuantity,
            request.ToolingCost,
            request.QuoteValidUntil,
            request.Notes));
        return NoContent();
    }

    [HttpGet("rfqs/{id:int}/compare")]
    public async Task<ActionResult<List<RfqVendorResponseModel>>> CompareRfqResponses(int id)
    {
        var result = await mediator.Send(new CompareRfqResponsesQuery(id));
        return Ok(result);
    }

    [HttpPost("rfqs/{id:int}/award/{responseId:int}")]
    public async Task<ActionResult> AwardRfq(int id, int responseId)
    {
        var poId = await mediator.Send(new AwardRfqCommand(id, responseId));
        return CreatedAtAction(nameof(GetRfqById), new { id }, new { purchaseOrderId = poId });
    }
}
