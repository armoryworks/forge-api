using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Costing.Tier3;
using Forge.Core.Models.Costing;

namespace Forge.Api.Controllers;

/// <summary>
/// Tier-3 activity-based costing — cost centers, overhead pools + budgets, costing periods, the
/// freeze that derives pool rates and composes per-work-center rates, and the resulting frozen rates.
/// Gated by CAP-COSTING-TIER3-ABC. The pure cost roll lives in <c>Forge.Core.Costing.CostRollEvaluator</c>.
/// </summary>
[ApiController]
[Route("api/v1/costing/tier3")]
[Authorize(Roles = "Admin,Manager")]
[RequiresCapability("CAP-COSTING-TIER3-ABC")]
public class Tier3CostingController(IMediator mediator) : ControllerBase
{
    [HttpGet("periods")]
    public async Task<ActionResult<List<CostingPeriodResponseModel>>> ListPeriods()
        => Ok(await mediator.Send(new ListCostingPeriodsQuery()));

    [HttpPost("periods")]
    public async Task<ActionResult<CostingPeriodResponseModel>> CreatePeriod([FromBody] CreateCostingPeriodCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(ListPeriods), new { }, result);
    }

    [HttpPost("periods/{id:int}/freeze")]
    public async Task<ActionResult<FreezeCostingPeriodResultModel>> Freeze(int id)
        => Ok(await mediator.Send(new FreezeCostingPeriodCommand(id)));

    [HttpGet("periods/{id:int}/rates")]
    public async Task<ActionResult<List<WorkCenterCostRateResponseModel>>> ListRates(int id)
        => Ok(await mediator.Send(new ListWorkCenterCostRatesQuery(id)));

    [HttpGet("cost-centers")]
    public async Task<ActionResult<List<CostingCostCenterResponseModel>>> ListCostCenters()
        => Ok(await mediator.Send(new ListCostingCostCentersQuery()));

    [HttpPost("cost-centers")]
    public async Task<ActionResult<CostingCostCenterResponseModel>> CreateCostCenter([FromBody] CreateCostingCostCenterCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(ListCostCenters), new { }, result);
    }

    [HttpGet("pools")]
    public async Task<ActionResult<List<OverheadPoolResponseModel>>> ListPools([FromQuery] int? costCenterId)
        => Ok(await mediator.Send(new ListOverheadPoolsQuery(costCenterId)));

    [HttpPost("pools")]
    public async Task<ActionResult<OverheadPoolResponseModel>> CreatePool([FromBody] CreateOverheadPoolCommand command)
    {
        var result = await mediator.Send(command);
        return CreatedAtAction(nameof(ListPools), new { }, result);
    }

    [HttpPut("budgets")]
    public async Task<ActionResult<OverheadPoolBudgetResponseModel>> UpsertBudget([FromBody] UpsertOverheadPoolBudgetCommand command)
        => Ok(await mediator.Send(command));

    // ─────────────────────────── Costing templates (quick-start packages) ───────────────────────────

    /// <summary>Lists costing templates (system + user) with their lines.</summary>
    [HttpGet("templates")]
    public async Task<ActionResult<IReadOnlyList<CostingTemplateResponseModel>>> ListTemplates(CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ListCostingTemplatesQuery(), cancellationToken));

    /// <summary>Creates (no id) or replaces (id) a costing template with its whole line graph.</summary>
    [HttpPost("templates")]
    public async Task<ActionResult<CostingTemplateResponseModel>> SaveTemplate(
        [FromBody] SaveCostingTemplateRequestModel model, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new SaveCostingTemplateCommand(model), cancellationToken));

    /// <summary>Soft-deletes a user template. System templates are never deletable.</summary>
    [HttpDelete("templates/{id:int}")]
    public async Task<IActionResult> DeleteTemplate(int id, CancellationToken cancellationToken)
    {
        await mediator.Send(new DeleteCostingTemplateCommand(id), cancellationToken);
        return NoContent();
    }

    /// <summary>Applies a template: a few answers populate the cost center, period,
    /// overhead pools + budgets, and (FULLGL on) the GL expense accounts + budget lines.</summary>
    [HttpPost("templates/{id:int}/apply")]
    public async Task<ActionResult<CostingQuickStartResponseModel>> ApplyTemplate(
        int id, [FromBody] ApplyCostingTemplateRequestModel model, CancellationToken cancellationToken)
        => Ok(await mediator.Send(new ApplyCostingTemplateCommand(id, model), cancellationToken));
}
