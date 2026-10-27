using System.Security.Claims;

using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

using Forge.Api.Capabilities;
using Forge.Api.Features.Ai;
using Forge.Api.Features.AiAssistants;
using Forge.Core.Models;

namespace Forge.Api.Controllers;

[ApiController]
[Route("api/v1/ai")]
[Authorize]
[RequiresCapability("CAP-EXT-AI-ASSISTANT")]
public class AiController(IMediator mediator, AiSseStreamWriter sseWriter) : ControllerBase
{
    private static readonly string[] RolePriority = ["Admin", "Manager", "OfficeManager", "PM", "Engineer", "ProductionWorker"];

    private string GetHighestPrivilegeRole()
    {
        var userRoles = User.Claims
            .Where(c => c.Type is ClaimTypes.Role or "role")
            .Select(c => c.Value)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        return RolePriority.FirstOrDefault(userRoles.Contains) ?? "General";
    }
    [HttpGet("status")]
    public async Task<IActionResult> GetStatus(CancellationToken ct)
    {
        var result = await mediator.Send(new CheckAiAvailabilityQuery(), ct);
        return Ok(result);
    }

    [HttpPost("generate")]
    public async Task<IActionResult> Generate([FromBody] GenerateTextCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("summarize")]
    public async Task<IActionResult> Summarize([FromBody] SummarizeTextCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("search-suggest")]
    public async Task<IActionResult> SearchSuggest([FromBody] AiSearchSuggestCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("help")]
    public async Task<IActionResult> HelpChat([FromBody] AiHelpChatBody body, CancellationToken ct)
    {
        var command = new AiHelpChatCommand(body.Question, body.History, GetHighestPrivilegeRole());
        var result = await mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("help/stream")]
    public async Task HelpChatStream([FromBody] AiHelpChatBody body, CancellationToken ct)
    {
        var command = new AiHelpChatStreamCommand(body.Question, body.History, GetHighestPrivilegeRole());
        var tokenStream = await mediator.Send(command, ct);
        await sseWriter.WriteAsync(Response, tokenStream, ct);
    }

    [HttpPost("search")]
    public async Task<IActionResult> RagSearch([FromBody] RagSearchCommand command, CancellationToken ct)
    {
        var result = await mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpPost("index")]
    public async Task<IActionResult> IndexDocument([FromBody] IndexDocumentRequestModel request, CancellationToken ct)
    {
        var chunkCount = await mediator.Send(new IndexDocumentCommand(request.EntityType, request.EntityId), ct);
        return Ok(new { chunksIndexed = chunkCount });
    }

    [HttpGet("index/status")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<AiIndexStatusResponseModel>> GetIndexStatus(CancellationToken ct)
    {
        var result = await mediator.Send(new GetAiIndexStatusQuery(), ct);
        return Ok(result);
    }

    [HttpPost("bulk-index")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> BulkIndex([FromBody] BulkIndexDocumentsCommand command, CancellationToken ct)
    {
        var totalChunks = await mediator.Send(command, ct);
        return Ok(new { totalChunksIndexed = totalChunks });
    }

    [HttpPost("assistants/{assistantId:int}/chat")]
    public async Task<IActionResult> AssistantChat(int assistantId, [FromBody] AssistantChatBody body, CancellationToken ct)
    {
        var result = await mediator.Send(new AssistantChatCommand(assistantId, body.Question, body.History), ct);
        return Ok(result);
    }

    [HttpPost("hardware-advice")]
    public async Task<IActionResult> HardwareAdvice([FromBody] GetAiHardwareAdviceQuery query, CancellationToken ct)
    {
        var result = await mediator.Send(query, ct);
        return Ok(result);
    }
}

public record AiHelpChatBody(string Question, List<AiHelpMessage>? History = null);
public record AssistantChatBody(string Question, List<AiHelpMessage>? History = null);
