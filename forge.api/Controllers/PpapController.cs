using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Quality;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/ppap-submissions")]
[Authorize]
[RequiresCapability("CAP-QC-PPAP")]
public class PpapController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<List<PpapSubmissionResponseModel>>> GetSubmissions(
        [FromQuery] int? partId, [FromQuery] int? customerId, [FromQuery] PpapStatus? status)
    {
        var result = await mediator.Send(new GetPpapSubmissionsQuery(partId, customerId, status));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<PpapSubmissionResponseModel>> GetSubmission(int id)
    {
        var result = await mediator.Send(new GetPpapSubmissionQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<PpapSubmissionResponseModel>> CreateSubmission(
        [FromBody] CreatePpapSubmissionRequestModel request)
    {
        var result = await mediator.Send(new CreatePpapSubmissionCommand(request));
        return Created($"/api/v1/ppap-submissions/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<PpapSubmissionResponseModel>> UpdateSubmission(
        int id, [FromBody] UpdatePpapSubmissionRequestModel request)
    {
        var result = await mediator.Send(new UpdatePpapSubmissionCommand(id, request));
        return Ok(result);
    }

    [HttpPut("{id:int}/elements/{number:int}")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<ActionResult<PpapElementResponseModel>> UpdateElement(
        int id, int number, [FromBody] UpdatePpapElementRequestModel request)
    {
        var result = await mediator.Send(new UpdatePpapElementCommand(id, number, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/submit")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<IActionResult> Submit(int id)
    {
        await mediator.Send(new SubmitPpapCommand(id));
        return NoContent();
    }

    [HttpPost("{id:int}/response")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> RecordResponse(
        int id, [FromBody] RecordPpapResponseRequestModel request)
    {
        await mediator.Send(new RecordPpapResponseCommand(id, request));
        return NoContent();
    }

    [HttpPost("{id:int}/psw/sign")]
    [Authorize(Roles = "Admin,Manager,Engineer")]
    public async Task<IActionResult> SignPsw(int id)
    {
        await mediator.Send(new SignPpapPswCommand(id));
        return NoContent();
    }

    [HttpGet("~/api/v1/ppap/level-requirements/{level:int}")]
    public async Task<ActionResult<List<PpapLevelRequirementResponseModel>>> GetLevelRequirements(int level)
    {
        var result = await mediator.Send(new GetPpapLevelRequirementsQuery(level));
        return Ok(result);
    }
}
