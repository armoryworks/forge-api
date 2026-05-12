using System.Text.RegularExpressions;

using MediatR;
using Microsoft.Extensions.Options;
using Pgvector;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Ai;

public record IndexDocumentationCommand : IRequest<int>;

public class IndexDocumentationHandler(
    IAiService aiService,
    IEmbeddingRepository embeddingRepo,
    IOptions<AiOptions> ollamaOptions,
    ILogger<IndexDocumentationHandler> logger) : IRequestHandler<IndexDocumentationCommand, int>
{
    private const string EntityType = "Documentation";
    private const int ChunkSize = 450;  // all-minilm:l6-v2 has ~256 token limit; ~450 chars is safe
    private const int ChunkOverlap = 50;

    public async Task<int> Handle(IndexDocumentationCommand request, CancellationToken ct)
    {
        var docsPath = ollamaOptions.Value.DocsPath;
        if (string.IsNullOrEmpty(docsPath) || !Directory.Exists(docsPath))
        {
            logger.LogWarning("Documentation path '{DocsPath}' not found — skipping documentation indexing", docsPath);
            return 0;
        }

        var markdownFiles = Directory.GetFiles(docsPath, "*.md", SearchOption.AllDirectories);
        if (markdownFiles.Length == 0)
        {
            logger.LogInformation("No markdown files found in '{DocsPath}'", docsPath);
            return 0;
        }

        logger.LogInformation("Indexing {Count} documentation files from '{DocsPath}'", markdownFiles.Length, docsPath);
        var totalChunks = 0;

        foreach (var filePath in markdownFiles)
        {
            try
            {
                var content = await File.ReadAllTextAsync(filePath, ct);
                if (string.IsNullOrWhiteSpace(content))
                    continue;

                var fileName = Path.GetFileNameWithoutExtension(filePath);
                // Deterministic, stable entity ID from filename
                var entityId = (Math.Abs(fileName.GetHashCode()) % 900_000) + 100_000;

                var chunks = ChunkMarkdown(content, fileName);
                var embeddings = new List<DocumentEmbedding>();
                var chunkIndex = 0;

                foreach (var chunk in chunks)
                {
                    var embedding = await aiService.GetEmbeddingAsync(chunk, ct);
                    embeddings.Add(new DocumentEmbedding
                    {
                        EntityType = EntityType,
                        EntityId = entityId,
                        ChunkText = chunk,
                        ChunkIndex = chunkIndex++,
                        SourceField = fileName,
                        Embedding = new Vector(embedding),
                        ModelName = ollamaOptions.Value.EmbeddingModel,
                    });
                }

                if (embeddings.Count > 0)
                {
                    await embeddingRepo.UpsertEmbeddingsAsync(EntityType, entityId, embeddings, ct);
                    totalChunks += embeddings.Count;
                    logger.LogInformation("Indexed {ChunkCount} chunks for doc '{FileName}'", embeddings.Count, fileName);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to index documentation file '{FilePath}'", filePath);
            }
        }

        return totalChunks;
    }

    private static List<string> ChunkMarkdown(string text, string fileName)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var prefix = $"[Source: {fileName}]\n\n";

        // Split by markdown headings for natural section boundaries, then ensure max size
        var sections = Regex.Split(text, @"(?=\n#{1,3} )");
        var rawChunks = new List<string>();

        var current = string.Empty;
        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section))
                continue;

            if (current.Length + section.Length > ChunkSize && current.Length > 0)
            {
                rawChunks.Add(current.Trim());
                current = current.Length > ChunkOverlap
                    ? current[^ChunkOverlap..] + "\n\n" + section
                    : section;
            }
            else
            {
                current = string.IsNullOrEmpty(current) ? section : current + "\n\n" + section;
            }
        }

        if (!string.IsNullOrWhiteSpace(current))
            rawChunks.Add(current.Trim());

        // Secondary pass: character-split any chunks still exceeding ChunkSize
        var finalChunks = new List<string>();
        foreach (var chunk in rawChunks)
        {
            if (chunk.Length <= ChunkSize)
            {
                finalChunks.Add($"{prefix}{chunk}");
                continue;
            }

            for (var i = 0; i < chunk.Length; i += ChunkSize - ChunkOverlap)
            {
                var length = Math.Min(ChunkSize, chunk.Length - i);
                finalChunks.Add($"{prefix}{chunk.Substring(i, length).Trim()}");
                if (i + length >= chunk.Length) break;
            }
        }

        return finalChunks.Count > 0 ? finalChunks : [$"{prefix}{text.Trim()[..Math.Min(ChunkSize, text.Length)]}"];
    }
}
