using System.Text.Json;

using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Services;
using Forge.Core.Entities;
using Forge.Data.Context;

namespace Forge.Api.Features.SystemApiKeys;

public record RevokeSystemApiKeyCommand(int Id) : IRequest;

public class RevokeSystemApiKeyHandler(AppDbContext db, ISystemAuditWriter auditWriter)
    : IRequestHandler<RevokeSystemApiKeyCommand>
{
    public async Task Handle(RevokeSystemApiKeyCommand request, CancellationToken cancellationToken)
    {
        var key = await db.SystemApiKeys
            .FirstOrDefaultAsync(k => k.Id == request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"SystemApiKey {request.Id} not found");

        // Idempotent: revoking an already-revoked key still emits an audit row
        // so operators can see "yes, the revoke arrived" even if it was a no-op.
        key.IsActive = false;
        await db.SaveChangesAsync(cancellationToken);

        var actorId = db.CurrentUserId ?? 0;
        var details = JsonSerializer.Serialize(new
        {
            name = key.Name,
            keyPrefix = key.KeyPrefix,
            userId = key.UserId,
        });
        await auditWriter.WriteAsync(
            action: "SystemApiKeyRevoked",
            userId: actorId,
            entityType: nameof(SystemApiKey),
            entityId: key.Id,
            details: details,
            ct: cancellationToken);
    }
}
