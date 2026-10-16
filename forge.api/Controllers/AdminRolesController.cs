using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.Admin;

namespace Forge.Api.Controllers;

/// <summary>
/// The identity role catalog and the role templates, read by the role filters and pickers that
/// managers use (employee list, announcements, compliance). It lives outside <see cref="AdminController"/>
/// because that controller's class-level Admin role combines with any action-level role list
/// by AND, so a Manager grant on the action could never take effect there. Bootstrap-exempt
/// for the same reason as the rest of the admin surface: role administration must never be
/// switched off.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Manager")]
public class AdminRolesController(IMediator mediator) : ControllerBase
{
    [HttpGet("roles")]
    [CapabilityBootstrap]
    public async Task<ActionResult<List<RoleItem>>> GetRoles()
    {
        var result = await mediator.Send(new GetRolesQuery());
        return Ok(result);
    }

    [HttpGet("role-templates")]
    [CapabilityBootstrap]
    public async Task<ActionResult<List<RoleTemplateResponseModel>>> GetRoleTemplates(
        [FromQuery] bool includeDeactivated = false)
    {
        var result = await mediator.Send(new GetRoleTemplatesQuery(includeDeactivated));
        return Ok(result);
    }
}
