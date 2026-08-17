using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.Search;
using Forge.Core.Models;
using Forge.Api.Capabilities;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/search")]
[Authorize]
// global header search is the search half of the list-UX capability
[RequiresCapability("CAP-CROSS-LIST-UX")]
public class SearchController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<SearchResultModel>>> Search([FromQuery] string q, [FromQuery] int limit = 20)
    {
        if (string.IsNullOrWhiteSpace(q) || q.Length < 2)
            return Ok(new List<SearchResultModel>());

        var result = await mediator.Send(new GlobalSearchQuery(q.Trim(), Math.Min(limit, 50)));
        return Ok(result);
    }
}
