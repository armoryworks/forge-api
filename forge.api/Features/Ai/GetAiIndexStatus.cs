using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Capabilities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Ai;

[RequiresCapability("CAP-EXT-AI-ASSISTANT")]
public record GetAiIndexStatusQuery : IRequest<AiIndexStatusResponseModel>;

public class GetAiIndexStatusHandler(AppDbContext db) : IRequestHandler<GetAiIndexStatusQuery, AiIndexStatusResponseModel>
{
    public async Task<AiIndexStatusResponseModel> Handle(GetAiIndexStatusQuery request, CancellationToken ct)
    {
        var indexed = await db.DocumentEmbeddings
            .AsNoTracking()
            .GroupBy(e => e.EntityType)
            .Select(g => new
            {
                EntityType = g.Key,
                Entities = g.Select(e => e.EntityId).Distinct().Count(),
                Chunks = g.Count(),
                LastIndexedAt = g.Max(e => (DateTimeOffset?)e.UpdatedAt),
            })
            .ToListAsync(ct);

        var types = BulkIndexDocumentsHandler.SupportedEntityTypes
            .Concat(indexed.Select(i => i.EntityType).Where(t => !BulkIndexDocumentsHandler.SupportedEntityTypes.Contains(t)))
            .ToList();

        var rows = new List<AiIndexEntityStatusModel>();
        foreach (var type in types)
        {
            var hit = indexed.FirstOrDefault(i => i.EntityType == type);
            rows.Add(new AiIndexEntityStatusModel(
                type,
                hit?.Entities ?? 0,
                await TotalAsync(type, ct),
                hit?.Chunks ?? 0,
                hit?.LastIndexedAt));
        }

        return new AiIndexStatusResponseModel(
            rows,
            rows.Sum(r => r.ChunkCount),
            rows.Max(r => r.LastIndexedAt));
    }

    private async Task<int?> TotalAsync(string entityType, CancellationToken ct) => entityType switch
    {
        "Job" => await db.Jobs.CountAsync(ct),
        "Part" => await db.Parts.CountAsync(ct),
        "FileAttachment" => await db.FileAttachments.CountAsync(ct),
        "Customer" => await db.Customers.CountAsync(ct),
        "Asset" => await db.Assets.CountAsync(ct),
        _ => null,
    };
}
