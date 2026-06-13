using MediatR;

using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Activity;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/{entityType}/{entityId:int}")]
[Authorize]
[RequiresCapability("CAP-CROSS-ACTIVITY-LOG")]
public class EntityActivityController(IMediator mediator) : ControllerBase
{
    private static readonly HashSet<string> AllowedEntityTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "Job", "Part", "Asset", "Lead", "Customer", "Expense",
        "SalesOrder", "Invoice", "Quote", "Shipment", "Payment",
        "PurchaseOrder", "Vendor", "CustomerReturn", "Lot",
        "VendorBill", "VendorPayment",
    };

    [HttpGet("activity")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetActivity(
        string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType))
            return BadRequest($"Entity type '{entityType}' is not supported.");

        var result = await mediator.Send(new GetEntityActivityQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpGet("history")]
    public async Task<ActionResult<List<ActivityResponseModel>>> GetHistory(
        string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType))
            return BadRequest($"Entity type '{entityType}' is not supported.");

        var result = await mediator.Send(new GetEntityHistoryQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpGet("notes")]
    public async Task<ActionResult<List<EntityNoteResponseModel>>> GetNotes(
        string entityType, int entityId)
    {
        if (!AllowedEntityTypes.Contains(entityType))
            return BadRequest($"Entity type '{entityType}' is not supported.");

        var result = await mediator.Send(new GetEntityNotesQuery(entityType, entityId));
        return Ok(result);
    }

    [HttpPost("notes")]
    public async Task<ActionResult<EntityNoteResponseModel>> CreateNote(
        string entityType, int entityId,
        [FromBody] CreateEntityNoteRequestModel request)
    {
        if (!AllowedEntityTypes.Contains(entityType))
            return BadRequest($"Entity type '{entityType}' is not supported.");

        var result = await mediator.Send(new CreateEntityNoteCommand(
            entityType, entityId, request.Text, request.MentionedUserIds));
        return Created($"api/v1/{entityType}/{entityId}/notes/{result.Id}", result);
    }

    [HttpDelete("notes/{noteId:int}")]
    public async Task<IActionResult> DeleteNote(string entityType, int entityId, int noteId)
    {
        if (!AllowedEntityTypes.Contains(entityType))
            return BadRequest($"Entity type '{entityType}' is not supported.");

        await mediator.Send(new DeleteEntityNoteCommand(noteId));
        return NoContent();
    }

    [HttpPost("comments")]
    public async Task<ActionResult<ActivityResponseModel>> CreateComment(
        string entityType, int entityId,
        [FromBody] CreateEntityCommentRequestModel request)
    {
        if (!AllowedEntityTypes.Contains(entityType))
            return BadRequest($"Entity type '{entityType}' is not supported.");

        var result = await mediator.Send(new CreateEntityCommentCommand(
            entityType, entityId, request.Comment, request.MentionedUserIds));
        return Created($"api/v1/{entityType}/{entityId}/activity/{result.Id}", result);
    }
}

public record CreateEntityNoteRequestModel(string Text, int[] MentionedUserIds);
public record CreateEntityCommentRequestModel(string Comment, int[] MentionedUserIds);
