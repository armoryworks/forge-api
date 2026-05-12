using System.Security.Claims;

using Hangfire;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.AutoPo;
using Forge.Api.Jobs;
using Forge.Core.Enums;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/auto-po")]
[Authorize(Roles = "Admin,Manager,OfficeManager")]
[RequiresCapability("CAP-P2P-AUTOPO")]
public class AutoPoController(IMediator mediator) : ControllerBase
{
    [HttpGet("suggestions")]
    public async Task<ActionResult<List<AutoPoSuggestionResponseModel>>> GetSuggestions(
        [FromQuery] AutoPoSuggestionStatus? status)
    {
        var result = await mediator.Send(new GetAutoPoSuggestionsQuery(status));
        return Ok(result);
    }

    [HttpPost("suggestions/{id:int}/convert")]
    public async Task<ActionResult<int>> ConvertSuggestion(int id)
    {
        var userId = GetUserId();
        var poId = await mediator.Send(new ConvertAutoPoSuggestionCommand(id, userId));
        return Ok(poId);
    }

    [HttpPost("suggestions/{id:int}/dismiss")]
    public async Task<IActionResult> DismissSuggestion(int id)
    {
        await mediator.Send(new DismissAutoPoSuggestionCommand(id));
        return NoContent();
    }

    [HttpPost("suggestions/bulk-convert")]
    public async Task<ActionResult<List<int>>> BulkConvert([FromBody] List<int> suggestionIds)
    {
        var userId = GetUserId();
        var poIds = await mediator.Send(new BulkConvertAutoPoSuggestionsCommand(suggestionIds, userId));
        return Ok(poIds);
    }

    [HttpPost("run")]
    [Authorize(Roles = "Admin")]
    public IActionResult RunNow()
    {
        BackgroundJob.Enqueue<AutoPurchaseOrderJob>(job => job.Execute(CancellationToken.None));
        return Accepted(new { message = "Auto-PO analysis triggered" });
    }

    [HttpGet("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AutoPoSettingsResponseModel>> GetSettings()
    {
        var result = await mediator.Send(new GetAutoPoSettingsQuery());
        return Ok(result);
    }

    [HttpPatch("settings")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AutoPoSettingsResponseModel>> UpdateSettings(
        [FromBody] UpdateAutoPoSettingsRequestModel model)
    {
        var result = await mediator.Send(new UpdateAutoPoSettingsCommand(
            model.Enabled, model.DefaultMode, model.BufferDays, model.NotifyChat));
        return Ok(result);
    }

    private int GetUserId() => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
}
