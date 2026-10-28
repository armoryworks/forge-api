using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Jobs;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Per-job P&amp;L (quoted price, labor and material actuals, margin). Kept out of
/// <see cref="JobsController"/> because that controller's class-level role list, which admits
/// production workers and not the Controller, combines with an action-level list by AND.
/// </summary>
[ApiController]
[Route("api/v1/jobs")]
[Authorize(Roles = ReadRoles)]
[RequiresCapability("CAP-MFG-WO-RELEASE")]
public class JobCostSummaryController(IMediator mediator) : ControllerBase
{
    public const string ReadRoles = "Admin,Manager,Engineer,PM,Controller,OfficeManager";

    [HttpGet("{id:int}/cost-summary")]
    public async Task<ActionResult<JobCostSummaryModel>> GetCostSummary(int id, CancellationToken ct)
        => Ok(await mediator.Send(new GetJobCostSummaryQuery(id), ct));
}
