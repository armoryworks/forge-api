using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Users;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/users")]
[Authorize]
[RequiresCapability("CAP-IDEN-USERS")]
public class UsersController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<UserResponseModel>>> GetUsers()
    {
        var result = await mediator.Send(new GetUsersQuery());
        return Ok(result);
    }
}
