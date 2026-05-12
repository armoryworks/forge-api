using System.Text;

using FluentValidation;
using MediatR;
using Pgvector;

using Forge.Core.Interfaces;

namespace Forge.Api.Features.Ai;

public record AiHelpChatStreamCommand(string Question, List<AiHelpMessage>? History = null, string? UserRole = null) : IRequest<IAsyncEnumerable<string>>;

public class AiHelpChatStreamValidator : AbstractValidator<AiHelpChatStreamCommand>
{
    public AiHelpChatStreamValidator()
    {
        RuleFor(x => x.Question).NotEmpty().MaximumLength(2000);
    }
}

public class AiHelpChatStreamHandler(
    IAiService aiService,
    IEmbeddingRepository embeddingRepo) : IRequestHandler<AiHelpChatStreamCommand, IAsyncEnumerable<string>>
{
    // Role entity type visibility — mirrors AiHelpChatHandler
    private static readonly Dictionary<string, string[]?> RoleEntityTypes = new()
    {
        ["Admin"]            = null,
        ["Manager"]          = null,
        ["OfficeManager"]    = ["Customer", "Invoice", "Payment", "SalesOrder", "Shipment", "Vendor", "Expense", "Documentation"],
        ["PM"]               = ["Job", "Customer", "Lead", "Quote", "SalesOrder", "Documentation"],
        ["Engineer"]         = ["Job", "Part", "Asset", "Customer", "Documentation"],
        ["ProductionWorker"] = ["Job", "Part", "Documentation"],
        ["General"]          = ["Job", "Part", "Customer", "Documentation"],
    };

    public async Task<IAsyncEnumerable<string>> Handle(AiHelpChatStreamCommand request, CancellationToken ct)
    {
        var role = request.UserRole ?? "General";
        var systemContext = AiHelpChatHandler.GetSystemContextPublic(role);
        var ragContext = await BuildRagContextAsync(request.Question, role, ct);

        var fullPrompt = $"""
            {systemContext}

            {ragContext}
            {FormatHistory(request.History)}
            User question: {request.Question}

            Provide a helpful, concise answer. Use bullet points for lists. Reference specific pages/features by name and URL path.
            If relevant context from the knowledge base is provided above, incorporate it into your answer.
            """;

        return aiService.GenerateTextStreamAsync(fullPrompt, ct);
    }

    private async Task<string> BuildRagContextAsync(string question, string role, CancellationToken ct)
    {
        try
        {
            var queryEmbeddingArray = await aiService.GetEmbeddingAsync(question, ct);
            if (queryEmbeddingArray.Length == 0)
                return string.Empty;

            var queryVector = new Vector(queryEmbeddingArray);

            RoleEntityTypes.TryGetValue(role, out var entityTypeFilter);
            var topK = entityTypeFilter is null ? 7 : 5;
            var filterList = entityTypeFilter?.ToList();

            var similar = await embeddingRepo.SearchSimilarAsync(queryVector, topK, filterList, ct);

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
