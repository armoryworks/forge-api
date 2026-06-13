using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Estimates;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/estimates")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-O2C-QUOTE")]
public class EstimatesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<EstimateListItemModel>>> GetEstimates(
        [FromQuery] int? customerId,
        [FromQuery] QuoteStatus? status,
        CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetEstimatesQuery(customerId, status), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<EstimateDetailResponseModel>> GetEstimate(int id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new GetEstimateQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<EstimateListItemModel>> CreateEstimate(
        CreateEstimateRequestModel request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new CreateEstimateCommand(
            request.CustomerId, request.Title, request.Description,
            request.EstimatedAmount, request.ValidUntil, request.Notes, request.AssignedToId), ct);
        return CreatedAtAction(nameof(GetEstimate), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateEstimate(int id, UpdateEstimateRequestModel request, CancellationToken ct = default)
    {
        await mediator.Send(new UpdateEstimateCommand(
            id, request.Title, request.Description, request.EstimatedAmount,
            request.Status, request.ValidUntil, request.Notes, request.AssignedToId), ct);
        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteEstimate(int id, CancellationToken ct = default)
    {
        await mediator.Send(new DeleteEstimateCommand(id), ct);
        return NoContent();
    }

    [HttpPost("{id:int}/lines")]
    public async Task<ActionResult<EstimateDetailResponseModel>> AddEstimateLine(
        int id, CreateQuoteLineModel request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new AddEstimateLineCommand(id, request), ct);
        return Ok(result);
    }

    [HttpPut("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<EstimateDetailResponseModel>> UpdateEstimateLine(
        int id, int lineId, UpdateOrderLineRequestModel request, CancellationToken ct = default)
    {
        var result = await mediator.Send(new UpdateEstimateLineCommand(id, lineId, request), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}/lines/{lineId:int}")]
    public async Task<ActionResult<EstimateDetailResponseModel>> DeleteEstimateLine(
        int id, int lineId, CancellationToken ct = default)
    {
        var result = await mediator.Send(new DeleteEstimateLineCommand(id, lineId), ct);
        return Ok(result);
    }

    [HttpPost("{id:int}/convert")]
    public async Task<ActionResult<QuoteListItemModel>> ConvertToQuote(int id, CancellationToken ct = default)
    {
        var result = await mediator.Send(new ConvertEstimateToQuoteCommand(id), ct);
        return Ok(result);
    }
}
