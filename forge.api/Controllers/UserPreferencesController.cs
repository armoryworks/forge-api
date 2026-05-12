using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Features.UserPreferences;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/user-preferences")]
[Authorize]
public class UserPreferencesController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserPreferenceResponseModel>>> GetAll()
    {
        var userId = GetUserId();
        var result = await mediator.Send(new GetUserPreferencesQuery(userId));
        return Ok(result);
    }

    [HttpPatch]
    public async Task<ActionResult<List<UserPreferenceResponseModel>>> Update(
        [FromBody] UpdateUserPreferencesRequestModel request)
    {
        var userId = GetUserId();
        var result = await mediator.Send(new UpdateUserPreferencesCommand(userId, request.Preferences));
        return Ok(result);
    }

    [HttpDelete("{key}")]
    public async Task<IActionResult> Delete(string key)
    {
        var userId = GetUserId();
        await mediator.Send(new DeleteUserPreferenceCommand(userId, key));
        return NoContent();
    }

    private int GetUserId()
        => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
