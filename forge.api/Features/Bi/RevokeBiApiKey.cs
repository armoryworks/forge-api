using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.Bi;

public record RevokeBiApiKeyCommand(int Id) : IRequest;

public class RevokeBiApiKeyHandler(AppDbContext db, ISystemAuditWriter auditWriter)
    : IRequestHandler<RevokeBiApiKeyCommand>
{
    public async Task Handle(RevokeBiApiKeyCommand request, CancellationToken cancellationToken)
    {
        var key = await db.BiApiKeys
            .FirstOrDefaultAsync(k => k.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"BiApiKey {request.Id} not found");

        // Idempotent: revoking an already-revoked key still emits an audit row.
        key.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        // System-wide audit row. Phase 3 / WU-04 / A3.
        var actorId = db.CurrentUserId ?? 0;
        var details = JsonSerializer.Serialize(new
        {
            name = key.Name,
            keyPrefix = key.KeyPrefix,
        });
        await auditWriter.WriteAsync(
            action: "BiApiKeyRevoked",
            userId: actorId,
            entityType: nameof(BiApiKey),
            entityId: key.Id,
            details: details,
            ct: cancellationToken);
    }
}
