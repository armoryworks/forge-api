using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using QBEngineer.Api.Features.TariffRates;
using QBEngineer.Core.Models;

namespace QBEngineer.Api.Controllers;

/// <summary>
/// Bought-parts effort PR4 — TariffRate admin CRUD. Tariffs feed
/// <see cref="QBEngineer.Core.Interfaces.ITariffResolver"/> at landed-cost
/// calc time. Today admins import broker data manually via this API; a
/// bulk-import endpoint is a future follow-up. Admin-only — landed cost
/// is a system-wide cost-discipline tool, not per-role.
/// </summary>
[ApiController]
[Route("api/v1/tariff-rates")]
[Authorize(Roles = "Admin")]
public class TariffRatesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TariffRateResponseModel>>> GetAll(CancellationToken ct)
    {
        var result = await mediator.Send(new GetTariffRatesQuery(), ct);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<TariffRateResponseModel>> GetById(int id, CancellationToken ct)
    {
        var result = await mediator.Send(new GetTariffRateByIdQuery(id), ct);
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<TariffRateResponseModel>> Create(
        [FromBody] CreateTariffRateRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new CreateTariffRateCommand(request), ct);
        return Created($"/api/v1/tariff-rates/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<TariffRateResponseModel>> Update(
        int id, [FromBody] UpdateTariffRateRequestModel request, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateTariffRateCommand(id, request), ct);
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id, CancellationToken ct)
    {
        await mediator.Send(new DeleteTariffRateCommand(id), ct);
        return NoContent();
    }
}
