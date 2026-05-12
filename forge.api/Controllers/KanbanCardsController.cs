using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.KanbanReplenishment;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/kanban-cards")]
[Authorize(Roles = "Admin,Manager,OfficeManager,Engineer,ProductionWorker")]
[RequiresCapability("CAP-EXT-KANBAN-REPLENISHMENT")]
public class KanbanCardsController(IMediator mediator) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult> GetCards(
        [FromQuery] int? workCenterId,
        [FromQuery] int? partId,
        [FromQuery] string? status,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 25)
    {
        var result = await mediator.Send(new GetKanbanCardsQuery(workCenterId, partId, status, page, pageSize));
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<KanbanCardDetailResponseModel>> GetCard(int id)
    {
        var result = await mediator.Send(new GetKanbanCardQuery(id));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<KanbanCardResponseModel>> CreateCard([FromBody] CreateKanbanCardRequestModel request)
    {
        var result = await mediator.Send(new CreateKanbanCardCommand(request));
        return Created($"/api/v1/kanban-cards/{result.Id}", result);
    }

    [HttpPut("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<ActionResult<KanbanCardResponseModel>> UpdateCard(int id, [FromBody] UpdateKanbanCardRequestModel request)
    {
        var result = await mediator.Send(new UpdateKanbanCardCommand(id, request));
        return Ok(result);
    }

    [HttpPost("{id:int}/trigger")]
    public async Task<IActionResult> TriggerReplenishment(int id, [FromBody] TriggerKanbanReplenishmentRequestModel request)
    {
        var userId = int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid) ? uid : (int?)null;
        await mediator.Send(new TriggerKanbanReplenishmentCommand(id, request, userId));
        return NoContent();
    }

    [HttpPost("{id:int}/confirm")]
    public async Task<IActionResult> ConfirmReplenishment(int id, [FromBody] ConfirmKanbanReplenishmentRequestModel request)
    {
        await mediator.Send(new ConfirmKanbanReplenishmentCommand(id, request));
        return NoContent();
    }

    [HttpGet("triggered")]
    public async Task<ActionResult<IReadOnlyList<KanbanCardResponseModel>>> GetTriggeredCards()
    {
        var result = await mediator.Send(new GetTriggeredKanbanCardsQuery());
        return Ok(result);
    }

    [HttpGet("board")]
    public async Task<ActionResult<IReadOnlyList<KanbanBoardWorkCenterResponseModel>>> GetBoard()
    {
        var result = await mediator.Send(new GetKanbanBoardByWorkCenterQuery());
        return Ok(result);
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin,Manager")]
    public async Task<IActionResult> DeleteCard(int id)
    {
        await mediator.Send(new DeleteKanbanCardCommand(id));
        return NoContent();
    }
}
