using System.Text;

using FluentValidation;
using MediatR;
using Microsoft.Extensions.Logging;
using Pgvector;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Ai;

public record RagSearchCommand(
    string Query,
    string? EntityTypeFilter = null,
    bool IncludeAnswer = false) : IRequest<RagSearchResponseModel>;

public class RagSearchValidator : AbstractValidator<RagSearchCommand>
{
    public RagSearchValidator()
    {
        RuleFor(x => x.Query).NotEmpty().MaximumLength(2000);
        RuleFor(x => x.EntityTypeFilter).MaximumLength(50);
    }
}

public class RagSearchHandler(
    IAiService aiService,
    IEmbeddingRepository embeddingRepo,
    ILiveContextProvider liveContext,
    ILogger<RagSearchHandler> logger) : IRequestHandler<RagSearchCommand, RagSearchResponseModel>
{
    private static readonly RagSearchResponseModel EmptyResponse = new([], null);

    public async Task<RagSearchResponseModel> Handle(RagSearchCommand request, CancellationToken ct)
    {
        float[] queryEmbeddingArray;
        try
        {
            queryEmbeddingArray = await aiService.GetEmbeddingAsync(request.Query, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "AI embedding service unavailable for search query");
            return EmptyResponse;
        }

        var queryVector = new Vector(queryEmbeddingArray);

        var similar = await embeddingRepo.SearchSimilarAsync(
            queryVector, 10, request.EntityTypeFilter, ct);

        var results = similar.Select((doc, index) =>
        {
            // Cosine distance ranges 0-2; convert to similarity score 0-1
            // Since pgvector orders by distance ascending, approximate score from position
            var score = 1.0 - (index * 0.05);
            return new RagSearchResultModel(
                doc.EntityType,
                doc.EntityId,
                doc.ChunkText,
                doc.SourceField,
                Math.Max(0, score));
        }).ToList();

        string? generatedAnswer = null;

        if (request.IncludeAnswer && results.Count > 0)
        {
            try
            {
                var contextChunks = results.Take(5).ToList();

                // D-3 hybrid freshness: overlay current volatile facts (stock/status/cost) for the
                // entities surfaced, so the model answers from live data rather than stale embeddings.
                var liveRefs = contextChunks
                    .Select(c => new EntityReference(c.EntityType, c.EntityId))
                    .Distinct()
                    .ToList();
                var liveFacts = await liveContext.GetFactsAsync(liveRefs, ct);

                var contextBuilder = new StringBuilder();

                if (liveFacts.Count > 0)
                {
                    contextBuilder.AppendLine("Current data (live — authoritative over any figures in the context below):");
                    foreach (var fact in liveFacts)
                        contextBuilder.AppendLine($"[{fact.EntityType} #{fact.EntityId}]: {fact.Facts}");
                    contextBuilder.AppendLine();
                }

                foreach (var chunk in contextChunks)
                {
                    contextBuilder.AppendLine($"[{chunk.EntityType} #{chunk.EntityId} — {chunk.SourceField}]:");
                    contextBuilder.AppendLine(chunk.ChunkText);
                    contextBuilder.AppendLine();
                }

                var prompt = $"""
                    You are a helpful assistant for a manufacturing operations platform called QB Engineer.
                    Use the following context to answer the user's question. If the context doesn't contain
                    relevant information, say so. Be concise and factual.

                    Context:
                    {contextBuilder}

                    Question: {request.Query}

                    Answer:
                    """;

                generatedAnswer = await aiService.GenerateTextAsync(prompt, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                logger.LogWarning(ex, "AI text generation unavailable for search answer");
            }
        }

        return new RagSearchResponseModel(results, generatedAnswer);
    }
}
