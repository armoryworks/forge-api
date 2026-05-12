using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Ai;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Jobs;

public class DocumentIndexJob(
    AppDbContext db,
    IMediator mediator,
    IAiService aiService,
    IClock clock,
    ILogger<DocumentIndexJob> logger)
{
    private static readonly string[] SupportedEntityTypes = ["Job", "Part", "Customer", "Asset"];

    public async Task IndexDocumentationAsync(CancellationToken ct = default)
    {
        var available = await aiService.IsAvailableAsync(ct);
        if (!available)
        {
            logger.LogInformation("AI service unavailable — skipping documentation indexing");
            return;
        }

        try
        {
            var chunksIndexed = await mediator.Send(new IndexDocumentationCommand(), ct);
            if (chunksIndexed > 0)
                logger.LogInformation("Documentation indexing complete: {ChunkCount} chunks", chunksIndexed);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Documentation indexing failed");
        }
    }

    public async Task IndexRecentlyUpdatedAsync(CancellationToken ct = default)
    {
        var available = await aiService.IsAvailableAsync(ct);
        if (!available)
        {
            logger.LogInformation("AI service unavailable — skipping document indexing");
            return;
        }

        var since = clock.UtcNow.AddMinutes(-35); // Overlap slightly to avoid gaps
        var totalIndexed = 0;

        foreach (var entityType in SupportedEntityTypes)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var updatedIds = await GetRecentlyUpdatedIdsAsync(entityType, since, ct);

                if (updatedIds.Count == 0)
                    continue;

                logger.LogInformation(
                    "Document indexing: {Count} {EntityType} entities updated since {Since:u}",
                    updatedIds.Count, entityType, since);

                foreach (var entityId in updatedIds)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        var chunkCount = await mediator.Send(
                            new IndexDocumentCommand(entityType, entityId), ct);
                        totalIndexed += chunkCount;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to index {EntityType} #{EntityId}", entityType, entityId);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to query recently updated {EntityType} entities", entityType);
            }
        }

        if (totalIndexed > 0)
        {
            logger.LogInformation("Document indexing complete: {TotalChunks} chunks indexed", totalIndexed);
        }
    }

    private async Task<List<int>> GetRecentlyUpdatedIdsAsync(string entityType, DateTimeOffset since, CancellationToken ct)
    {
        return entityType switch
        {
            "Job" => await db.Jobs
                .Where(j => j.UpdatedAt >= since)
                .Select(j => j.Id)
                .ToListAsync(ct),
            "Part" => await db.Parts
                .Where(p => p.UpdatedAt >= since)
                .Select(p => p.Id)
                .ToListAsync(ct),
            "Customer" => await db.Customers
                .Where(c => c.UpdatedAt >= since)
                .Select(c => c.Id)
                .ToListAsync(ct),
            "Asset" => await db.Assets
                .Where(a => a.UpdatedAt >= since)
                .Select(a => a.Id)
                .ToListAsync(ct),
            _ => [],
        };
    }
}
