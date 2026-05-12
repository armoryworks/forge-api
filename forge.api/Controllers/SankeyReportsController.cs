using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Reports.Sankey;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/reports/sankey")]
[Authorize(Roles = "Admin,Manager,OfficeManager,PM")]
[RequiresCapability("CAP-RPT-OPERATIONAL")]
public class SankeyReportsController(IMediator mediator) : ControllerBase
{
    [HttpGet("quote-to-cash")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetQuoteToCash(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        var result = await mediator.Send(new GetQuoteToCashFlowQuery(start, end));
        return Ok(result);
    }

    [HttpGet("job-stage-flow")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetJobStageFlow()
    {
        var result = await mediator.Send(new GetJobStageFlowQuery());
        return Ok(result);
    }

    [HttpGet("material-to-product")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetMaterialToProduct()
    {
        var result = await mediator.Send(new GetMaterialToProductFlowQuery());
        return Ok(result);
    }

    [HttpGet("worker-orders")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetWorkerOrders()
    {
        var result = await mediator.Send(new GetWorkerOrdersFlowQuery());
        return Ok(result);
    }

    [HttpGet("expense-flow")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetExpenseFlow(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        var result = await mediator.Send(new GetExpenseFlowQuery(start, end));
        return Ok(result);
    }

    [HttpGet("vendor-supply-chain")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetVendorSupplyChain()
    {
        var result = await mediator.Send(new GetVendorSupplyChainFlowQuery());
        return Ok(result);
    }

    [HttpGet("quality-rejection")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetQualityRejection(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        var result = await mediator.Send(new GetQualityRejectionFlowQuery(start, end));
        return Ok(result);
    }

    [HttpGet("inventory-location")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetInventoryLocation()
    {
        var result = await mediator.Send(new GetInventoryLocationFlowQuery());
        return Ok(result);
    }

    [HttpGet("customer-revenue")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetCustomerRevenue(
        [FromQuery] DateTimeOffset? start, [FromQuery] DateTimeOffset? end)
    {
        var result = await mediator.Send(new GetCustomerRevenueFlowQuery(start, end));
        return Ok(result);
    }

    [HttpGet("training-completion")]
    public async Task<ActionResult<List<SankeyFlowItem>>> GetTrainingCompletion()
    {
        var result = await mediator.Send(new GetTrainingCompletionFlowQuery());
        return Ok(result);
    }
}
