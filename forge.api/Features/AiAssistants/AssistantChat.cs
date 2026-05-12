using System.Text;
using System.Text.Json;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pgvector;

using Forge.Api.Features.Ai;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.AiAssistants;

public record AssistantChatCommand(
    int AssistantId,
    string Question,
    List<AiHelpMessage>? History = null) : IRequest<AiHelpChatResponse>;

public class AssistantChatValidator : AbstractValidator<AssistantChatCommand>
{
    public AssistantChatValidator()
    {
        RuleFor(x => x.AssistantId).GreaterThan(0);
        RuleFor(x => x.Question).NotEmpty().MaximumLength(2000);
    }
}

public class AssistantChatHandler(
    AppDbContext db,
    IAiService aiService,
    IEmbeddingRepository embeddingRepo) : IRequestHandler<AssistantChatCommand, AiHelpChatResponse>
{
    public async Task<AiHelpChatResponse> Handle(AssistantChatCommand request, CancellationToken ct)
    {
        using var availCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        availCts.CancelAfter(TimeSpan.FromSeconds(5));
        var available = await aiService.IsAvailableAsync(availCts.Token).ConfigureAwait(false);
        if (!available)
            return new AiHelpChatResponse("The AI service is currently unavailable. Please try again later.");

        var assistant = await db.AiAssistants
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == request.AssistantId && a.IsActive, ct)
            ?? throw new KeyNotFoundException($"AI Assistant with ID {request.AssistantId} not found or inactive.");

        var entityTypeFilters = JsonSerializer.Deserialize<List<string>>(assistant.AllowedEntityTypes) ?? [];

        var ragContext = await BuildRagContextAsync(request.Question, entityTypeFilters, assistant.MaxContextChunks, ct);

        var prompt = $"""
            {ragContext}
            {FormatHistory(request.History)}
            User question: {request.Question}

            Provide a helpful, concise answer. Use bullet points for lists. Reference specific pages/features by name and URL path.
            If relevant context from the knowledge base is provided above, incorporate it into your answer.
            """;

        var answer = await aiService.GenerateTextAsync(prompt, assistant.SystemPrompt, assistant.Temperature, ct);
        return new AiHelpChatResponse(answer);
    }

    private async Task<string> BuildRagContextAsync(
        string question, List<string> entityTypeFilters, int maxChunks, CancellationToken ct)
    {
        try
        {
            var queryEmbeddingArray = await aiService.GetEmbeddingAsync(question, ct);
            if (queryEmbeddingArray.Length == 0)
                return string.Empty;

            var queryVector = new Vector(queryEmbeddingArray);
            var similar = await embeddingRepo.SearchSimilarAsync(queryVector, maxChunks, entityTypeFilters, ct);

            if (similar.Count == 0)
                return string.Empty;

            var sb = new StringBuilder("Relevant knowledge base context:\n");
            foreach (var doc in similar)
                sb.AppendLine($"[{doc.EntityType} #{doc.EntityId} — {doc.SourceField}]: {doc.ChunkText}");
            sb.AppendLine();

            return sb.ToString();
        }
        catch
        {
            return string.Empty;
        }
    }

    private static string FormatHistory(List<AiHelpMessage>? history)
    {
        if (history is null || history.Count == 0) return string.Empty;
        var sb = new StringBuilder("Previous conversation:\n");
        foreach (var msg in history.TakeLast(6))
            sb.AppendLine($"{msg.Role}: {msg.Content}");
        return sb.ToString();
    }
}
