using System.Security.Claims;

using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.EmployeeProfile;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/employee-profile")]
[Authorize]
[RequiresCapability("CAP-MD-EMPLOYEES")]
public class EmployeeProfileController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<EmployeeProfileResponseModel>> GetProfile(CancellationToken ct)
    {
        var result = await mediator.Send(new GetEmployeeProfileQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpPut]
    public async Task<ActionResult<EmployeeProfileResponseModel>> UpdateProfile(
        [FromBody] UpdateEmployeeProfileRequestModel data, CancellationToken ct)
    {
        var result = await mediator.Send(new UpdateEmployeeProfileCommand(GetUserId(), data), ct);
        return Ok(result);
    }

    [HttpGet("completeness")]
    public async Task<ActionResult<ProfileCompletenessResponseModel>> GetCompleteness(CancellationToken ct)
    {
        var result = await mediator.Send(new GetProfileCompletenessQuery(GetUserId()), ct);
        return Ok(result);
    }

    [HttpPost("acknowledge/{formType}")]
    public async Task<IActionResult> AcknowledgeForm(string formType, CancellationToken ct)
    {
        await mediator.Send(new AcknowledgeFormCommand(GetUserId(), formType), ct);
        return NoContent();
    }
}
