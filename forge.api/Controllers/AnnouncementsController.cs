using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Announcements;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/announcements")]
[Authorize]
[RequiresCapability("CAP-EXT-ANNOUNCEMENTS")]
public class AnnouncementsController(IMediator mediator) : ControllerBase
{
    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

    [HttpGet]
    public async Task<ActionResult<List<AnnouncementResponseModel>>> GetActive()
    {
        var result = await mediator.Send(new GetActiveAnnouncementsQuery(GetUserId()));
        return Ok(result);
    }

    [HttpGet("all")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<AnnouncementResponseModel>>> GetAll()
    {
        var result = await mediator.Send(new GetAllAnnouncementsQuery());
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<AnnouncementResponseModel>> Create([FromBody] CreateAnnouncementRequestModel model)
    {
        var result = await mediator.Send(new CreateAnnouncementCommand(
            GetUserId(),
            model.Title,
            model.Content,
            model.Severity,
            model.Scope,
            model.RequiresAcknowledgment,
            model.ExpiresAt,
            model.DepartmentId,
            model.TargetTeamIds,
            model.TemplateId));

        return CreatedAtAction(nameof(GetActive), null, result);
    }

    [HttpPost("{id}/acknowledge")]
    public async Task<IActionResult> Acknowledge(int id)
    {
        await mediator.Send(new AcknowledgeAnnouncementCommand(id, GetUserId()));
        return NoContent();
    }

    [HttpGet("{id}/acknowledgments")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<AnnouncementAcknowledgmentResponseModel>>> GetAcknowledgments(int id)
    {
        var result = await mediator.Send(new GetAnnouncementAcknowledgmentsQuery(id));
        return Ok(result);
    }

    // ── Templates ──

    [HttpGet("templates")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<List<AnnouncementTemplateResponseModel>>> GetTemplates()
    {
        var result = await mediator.Send(new GetAnnouncementTemplatesQuery());
        return Ok(result);
    }

    [HttpPost("templates")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AnnouncementTemplateResponseModel>> CreateTemplate([FromBody] CreateAnnouncementTemplateRequestModel model)
    {
        var result = await mediator.Send(new CreateAnnouncementTemplateCommand(
            model.Name,
            model.Content,
            model.DefaultSeverity,
            model.DefaultScope,
            model.DefaultRequiresAcknowledgment));

        return CreatedAtAction(nameof(GetTemplates), null, result);
    }

    [HttpDelete("templates/{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> DeleteTemplate(int id)
    {
        await mediator.Send(new DeleteAnnouncementTemplateCommand(id));
        return NoContent();
    }
}
