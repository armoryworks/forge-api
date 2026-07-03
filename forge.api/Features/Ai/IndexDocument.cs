using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Pgvector;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Ai;

public record IndexDocumentCommand(string EntityType, int EntityId) : IRequest<int>;

public class IndexDocumentValidator : AbstractValidator<IndexDocumentCommand>
{
    public IndexDocumentValidator()
    {
        RuleFor(x => x.EntityType).NotEmpty().MaximumLength(50);
        RuleFor(x => x.EntityId).GreaterThan(0);
    }
}

public class IndexDocumentHandler(
    AppDbContext db,
    IAiService aiService,
    IEmbeddingRepository embeddingRepo,
    IStorageService storage,
    ILogger<IndexDocumentHandler> logger) : IRequestHandler<IndexDocumentCommand, int>
{
    private const int ChunkSize = 500;
    private const int ChunkOverlap = 50;
    private const string EmbeddingModel = "all-minilm:l6-v2";

    // Guard rails for pulling attachment bodies into the RAG index.
    private const long MaxExtractBytes = 2 * 1024 * 1024; // 2 MB — don't stream huge files in for embedding
    private const int MaxExtractChars = 50_000;           // cap the text fed to the chunker

    public async Task<int> Handle(IndexDocumentCommand request, CancellationToken ct)
    {
        var textFields = await ExtractTextFieldsAsync(request.EntityType, request.EntityId, ct);

        if (textFields.Count == 0)
        {
            logger.LogWarning("No text found for {EntityType} #{EntityId}", request.EntityType, request.EntityId);
            return 0;
        }

        var embeddings = new List<DocumentEmbedding>();
        var chunkIndex = 0;

        foreach (var (sourceField, text) in textFields)
        {
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var chunks = ChunkText(text);

            foreach (var chunk in chunks)
            {
                var embedding = await aiService.GetEmbeddingAsync(chunk, ct);

                embeddings.Add(new DocumentEmbedding
                {
                    EntityType = request.EntityType,
                    EntityId = request.EntityId,
                    ChunkText = chunk,
                    ChunkIndex = chunkIndex++,
                    SourceField = sourceField,
                    Embedding = new Vector(embedding),
                    ModelName = EmbeddingModel,
                });
            }
        }

        if (embeddings.Count > 0)
        {
            await embeddingRepo.UpsertEmbeddingsAsync(request.EntityType, request.EntityId, embeddings, ct);
            logger.LogInformation("Indexed {ChunkCount} chunks for {EntityType} #{EntityId}",
                embeddings.Count, request.EntityType, request.EntityId);
        }

        return embeddings.Count;
    }

    private async Task<List<(string SourceField, string Text)>> ExtractTextFieldsAsync(
        string entityType, int entityId, CancellationToken ct)
    {
        return entityType switch
        {
            "Job" => await ExtractJobFieldsAsync(entityId, ct),
            "Part" => await ExtractPartFieldsAsync(entityId, ct),
            "FileAttachment" => await ExtractFileAttachmentFieldsAsync(entityId, ct),
            "Customer" => await ExtractCustomerFieldsAsync(entityId, ct),
            "Asset" => await ExtractAssetFieldsAsync(entityId, ct),
            _ => [],
        };
    }

    private async Task<List<(string, string)>> ExtractJobFieldsAsync(int id, CancellationToken ct)
    {
        var job = await db.Jobs.AsNoTracking().FirstOrDefaultAsync(j => j.Id == id, ct);
        if (job is null) return [];

        var fields = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(job.Title))
            fields.Add(("Title", job.Title));
        if (!string.IsNullOrWhiteSpace(job.Description))
            fields.Add(("Description", job.Description));
        if (!string.IsNullOrWhiteSpace(job.IterationNotes))
            fields.Add(("IterationNotes", job.IterationNotes));
        return fields;
    }

    private async Task<List<(string, string)>> ExtractPartFieldsAsync(int id, CancellationToken ct)
    {
        var part = await db.Parts
            .Include(p => p.Operations)
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.Id == id, ct);
        if (part is null) return [];

        var fields = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(part.PartNumber))
            fields.Add(("PartNumber", part.PartNumber));
        if (!string.IsNullOrWhiteSpace(part.Description))
            fields.Add(("Description", part.Description));
        // Pre-beta: Material free-text was retired. The MaterialSpec FK is
        // loaded into the projection elsewhere — when needed, the AI indexer
        // can switch to it via Include(p => p.MaterialSpec).

        if (part.Operations.Count > 0)
        {
            var instructions = string.Join("\n",
                part.Operations
                    .OrderBy(s => s.StepNumber)
                    .Select(s => $"Step {s.StepNumber}: {s.Title}. {s.Instructions ?? ""}"));
            fields.Add(("Operations", instructions));
        }

        return fields;
    }

    private async Task<List<(string, string)>> ExtractFileAttachmentFieldsAsync(int id, CancellationToken ct)
    {
        var file = await db.FileAttachments.AsNoTracking().FirstOrDefaultAsync(f => f.Id == id, ct);
        if (file is null) return [];

        var fields = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(file.FileName))
            fields.Add(("FileName", file.FileName));

        // Index the body of text-like attachments so their content is searchable, not just
        // the filename. Binary types (images, office docs, generic PDFs) are skipped here —
        // PDFs have their own dedicated form-extraction pipeline.
        if (IsTextExtractable(file.ContentType, file.FileName)
            && file.Size is > 0 and <= MaxExtractBytes
            && !string.IsNullOrWhiteSpace(file.BucketName)
            && !string.IsNullOrWhiteSpace(file.ObjectKey))
        {
            try
            {
                await using var stream = await storage.DownloadAsync(file.BucketName, file.ObjectKey, ct);
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync(ct);
                if (!string.IsNullOrWhiteSpace(content))
                {
                    if (content.Length > MaxExtractChars)
                        content = content[..MaxExtractChars];
                    fields.Add(("Content", content));
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not extract text from attachment #{Id} ({FileName})", id, file.FileName);
            }
        }

        return fields;
    }

    private static bool IsTextExtractable(string? contentType, string fileName)
    {
        if (!string.IsNullOrWhiteSpace(contentType))
        {
            var ct = contentType.ToLowerInvariant();
            if (ct.StartsWith("text/", StringComparison.Ordinal))
                return true;
            if (ct is "application/json" or "application/xml" or "application/csv" or "application/x-ndjson")
                return true;
        }

        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        return ext is ".txt" or ".md" or ".csv" or ".json" or ".xml" or ".log" or ".yaml" or ".yml";
    }

    private async Task<List<(string, string)>> ExtractCustomerFieldsAsync(int id, CancellationToken ct)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == id, ct);
        if (customer is null) return [];

        var fields = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(customer.Name))
            fields.Add(("Name", customer.Name));
        if (!string.IsNullOrWhiteSpace(customer.CompanyName))
            fields.Add(("CompanyName", customer.CompanyName));
        return fields;
    }

    private async Task<List<(string, string)>> ExtractAssetFieldsAsync(int id, CancellationToken ct)
    {
        var asset = await db.Assets.AsNoTracking().FirstOrDefaultAsync(a => a.Id == id, ct);
        if (asset is null) return [];

        var fields = new List<(string, string)>();
        if (!string.IsNullOrWhiteSpace(asset.Name))
            fields.Add(("Name", asset.Name));
        if (!string.IsNullOrWhiteSpace(asset.Notes))
            fields.Add(("Notes", asset.Notes));
        return fields;
    }

    internal static List<string> ChunkText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        if (text.Length <= ChunkSize)
            return [text.Trim()];

        var chunks = new List<string>();

        // Try splitting by paragraphs first
        var paragraphs = text.Split(["\n\n", "\r\n\r\n"], StringSplitOptions.RemoveEmptyEntries);

        if (paragraphs.Length > 1)
        {
            var current = string.Empty;
            foreach (var paragraph in paragraphs)
            {
                if (current.Length + paragraph.Length > ChunkSize && current.Length > 0)
                {
                    chunks.Add(current.Trim());
                    // Overlap: keep end of previous chunk
                    current = current.Length > ChunkOverlap
                        ? current[^ChunkOverlap..] + " " + paragraph
                        : paragraph;
                }
                else
                {
                    current = string.IsNullOrEmpty(current) ? paragraph : current + "\n\n" + paragraph;
                }
            }
            if (!string.IsNullOrWhiteSpace(current))
                chunks.Add(current.Trim());
        }
        else
        {
            // Fall back to character-based chunking
            for (var i = 0; i < text.Length; i += ChunkSize - ChunkOverlap)
            {
                var length = Math.Min(ChunkSize, text.Length - i);
                chunks.Add(text.Substring(i, length).Trim());
                if (i + length >= text.Length)
                    break;
            }
        }

        return chunks;
    }
}
