using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.CompanyLocations;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/company-locations")]
[Authorize(Roles = "Admin")]
[RequiresCapability("CAP-MD-LOCATIONS")]
public class CompanyLocationsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<CompanyLocationResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetCompanyLocationsQuery());
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CompanyLocationResponseModel>> Get(int id)
    {
        var result = await mediator.Send(new GetCompanyLocationQuery(id));
        return Ok(result);
    }

    [HttpPost]
    public async Task<ActionResult<CompanyLocationResponseModel>> Create(CompanyLocationRequestModel request)
    {
        var result = await mediator.Send(new CreateCompanyLocationCommand(
            request.Name, request.Line1, request.Line2, request.City,
            request.State, request.PostalCode, request.Country,
            request.Phone, request.IsActive));
        return CreatedAtAction(nameof(Get), new { id = result.Id }, result);
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CompanyLocationResponseModel>> Update(int id, CompanyLocationRequestModel request)
    {
        var result = await mediator.Send(new UpdateCompanyLocationCommand(
            id, request.Name, request.Line1, request.Line2, request.City,
            request.State, request.PostalCode, request.Country,
            request.Phone, request.IsActive));
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> Delete(int id)
    {
        await mediator.Send(new DeleteCompanyLocationCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/set-default")]
    public async Task<IActionResult> SetDefault(int id)
    {
        await mediator.Send(new SetDefaultCompanyLocationCommand(id));
        return NoContent();
    }
}
