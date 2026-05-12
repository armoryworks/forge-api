using Microsoft.EntityFrameworkCore;
using Pgvector;
using Pgvector.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Data.Repositories;

public class EmbeddingRepository(AppDbContext context) : IEmbeddingRepository
{
    public async Task<List<DocumentEmbedding>> SearchSimilarAsync(
        Vector queryVector, int topK, string? entityTypeFilter, CancellationToken ct)
    {
        var query = context.DocumentEmbeddings
            .AsNoTracking()
            .Where(e => e.Embedding != null);

        if (entityTypeFilter is not null)
        {
            query = query.Where(e => e.EntityType == entityTypeFilter);
        }

        return await query
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .ToListAsync(ct);
    }

    public async Task<List<DocumentEmbedding>> SearchSimilarAsync(
        Vector queryVector, int topK, List<string>? entityTypeFilters, CancellationToken ct)
    {
        var query = context.DocumentEmbeddings
            .AsNoTracking()
            .Where(e => e.Embedding != null);

        if (entityTypeFilters is { Count: > 0 })
        {
            query = query.Where(e => entityTypeFilters.Contains(e.EntityType));
        }

        return await query
            .OrderBy(e => e.Embedding!.CosineDistance(queryVector))
            .Take(topK)
            .ToListAsync(ct);
    }

    public async Task UpsertEmbeddingsAsync(
        string entityType, int entityId, List<DocumentEmbedding> embeddings, CancellationToken ct)
    {
        // Delete existing embeddings for this entity (hard delete — embeddings are derived data)
        var existing = await context.DocumentEmbeddings
            .IgnoreQueryFilters()
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .ToListAsync(ct);

        if (existing.Count > 0)
            context.DocumentEmbeddings.RemoveRange(existing);

        await context.DocumentEmbeddings.AddRangeAsync(embeddings, ct);
        await context.SaveChangesAsync(ct);
    }

    public async Task DeleteEmbeddingsAsync(string entityType, int entityId, CancellationToken ct)
    {
        var now = DateTimeOffset.UtcNow;
        var embeddings = await context.DocumentEmbeddings
            .Where(e => e.EntityType == entityType && e.EntityId == entityId)
            .ToListAsync(ct);

        foreach (var embedding in embeddings)
        {
            embedding.DeletedAt = now;
        }

        await context.SaveChangesAsync(ct);
    }

    public async Task<int> GetEmbeddingCountAsync(CancellationToken ct)
    {
        return await context.DocumentEmbeddings.CountAsync(ct);
    }
}
