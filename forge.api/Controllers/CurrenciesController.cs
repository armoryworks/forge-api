using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Admin;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/admin/currencies")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-MD-CURRENCIES")]
public class CurrenciesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CurrencyResponseModel>>> GetCurrencies()
    {
        var result = await mediator.Send(new GetCurrenciesQuery());
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CurrencyResponseModel>> CreateCurrency([FromBody] CreateCurrencyRequestModel request)
    {
        var result = await mediator.Send(new CreateCurrencyCommand(request));
        return CreatedAtAction(nameof(GetCurrencies), new { }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> UpdateCurrency(int id, [FromBody] UpdateCurrencyRequestModel request)
    {
        await mediator.Send(new UpdateCurrencyCommand(id, request));
        return NoContent();
    }
}
