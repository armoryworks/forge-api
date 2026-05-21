using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Dashboard;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/dashboard")]
[Authorize]
[RequiresCapability("CAP-RPT-DASHBOARDS")]
public class DashboardController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<DashboardResponseModel>> GetDashboard()
    {
        var result = await mediator.Send(new GetDashboardQuery());
        return Ok(result);
    }

    [HttpGet("open-orders")]
    public async Task<ActionResult<OpenOrdersSummaryModel>> GetOpenOrders()
    {
        var result = await mediator.Send(new GetOpenOrdersSummaryQuery());
        return Ok(result);
    }

    // F-037: company-wide margin is exec/management data — not for low-trust roles.
    [HttpGet("margin-summary")]
    [Authorize(Roles = "Admin,Manager,Controller,OfficeManager,PM")]
    public async Task<ActionResult<MarginSummaryResponseModel>> GetMarginSummary()
    {
        var result = await mediator.Send(new GetMarginSummaryQuery());
        return Ok(result);
    }

    [HttpGet("layout")]
    public async Task<ActionResult<DashboardLayoutResponseModel>> GetDefaultLayout()
    {
        var result = await mediator.Send(new GetDefaultDashboardLayoutQuery());
        return Ok(result);
    }
}
