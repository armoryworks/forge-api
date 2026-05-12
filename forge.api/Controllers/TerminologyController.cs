using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.Terminology;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/terminology")]
[Authorize]
public class TerminologyController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<TerminologyEntryResponseModel>>> GetTerminology()
    {
        var result = await mediator.Send(new GetTerminologyQuery());
        return Ok(result);
    }

    [HttpPut]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<TerminologyEntryResponseModel>>> UpdateTerminology(
        UpdateTerminologyCommand command)
    {
        var result = await mediator.Send(command);
        return Ok(result);
    }
}
