using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Features.Admin;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Endpoints exposing install-wide configuration the UI needs at boot.
/// Currently just the base currency; expand here as other "tell the UI
/// what's configured" needs surface.
/// </summary>
[ApiController]
[Route("api/v1/system")]
[Authorize]
public class SystemController(ICurrencyService currencyService, IMediator mediator) : ControllerBase
{
    /// <summary>
    /// Returns the install's base currency so the UI can decide whether
    /// to suffix record-level ISO codes when displaying prices.
    /// </summary>
    [HttpGet("currency-base")]
    public async Task<ActionResult<CurrencyBaseResponseModel>> GetCurrencyBase(CancellationToken ct)
    {
        var code = await currencyService.GetBaseCurrencyAsync(ct);
        return Ok(new CurrencyBaseResponseModel(code));
    }

    /// <summary>
    /// Returns the active currency catalog so operational document/payment
    /// forms (invoices, vendor bills, payments) can offer a currency selector
    /// + FX-rate input without requiring the Admin-only currencies endpoint.
    /// Read-only; any authenticated user may call it (mirrors currency-base).
    /// </summary>
    [HttpGet("currencies")]
    public async Task<ActionResult<List<CurrencyResponseModel>>> GetActiveCurrencies(CancellationToken ct)
    {
        var all = await mediator.Send(new GetCurrenciesQuery(), ct);
        return Ok(all.Where(c => c.IsActive).ToList());
    }
}
