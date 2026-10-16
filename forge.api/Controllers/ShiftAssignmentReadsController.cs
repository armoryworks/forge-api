using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Forge.Api.Capabilities;
using Forge.Api.Features.ShiftAssignments;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

/// <summary>
/// Shift-assignment reads for managers. Kept out of <see cref="AdminController"/> because its
/// class-level Admin role combines with an action-level role list by AND, so a Manager grant
/// there could never take effect. Writes stay on <see cref="AdminController"/>.
/// </summary>
[ApiController]
[Route("api/v1/admin")]
[Authorize(Roles = "Admin,Manager")]
public class ShiftAssignmentReadsController(IMediator mediator) : ControllerBase
{
    [HttpGet("shift-assignments")]
    [CapabilityBootstrap]
    public async Task<ActionResult<List<ShiftAssignmentResponseModel>>> GetShiftAssignments(
        [FromQuery] int? userId, CancellationToken ct)
        => Ok(await mediator.Send(new GetShiftAssignmentsQuery(userId), ct));
}
