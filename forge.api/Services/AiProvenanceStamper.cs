using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Services;

/// <summary>ai-fleet-orchestration D: records/queries AI-provenance markers (one per artifact).</summary>
public sealed class AiProvenanceStamper(AppDbContext db) : IAiProvenanceStamper
{
    public async Task StampAsync(string entityType, int entityId, string? model = null, CancellationToken ct = default)
    {
        if (!await db.AiProvenances.AnyAsync(p => p.EntityType == entityType && p.EntityId == entityId, ct))
            db.AiProvenances.Add(new AiProvenance { EntityType = entityType, EntityId = entityId, Model = model });

        await db.SaveChangesAsync(ct);
    }

    public Task<bool> IsAiGeneratedAsync(string entityType, int entityId, CancellationToken ct = default)
        => db.AiProvenances.AnyAsync(p => p.EntityType == entityType && p.EntityId == entityId, ct);
}
