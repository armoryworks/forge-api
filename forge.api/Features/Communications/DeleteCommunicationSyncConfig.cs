using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Data.Context;
using Forge.Data.Extensions;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — soft-delete a mailbox / phone connection. Only the owning
/// user can delete their own connection; admins use the admin-side
/// per-user listing (separate handler, future). Soft-delete preserves
/// the historical audit trail of "was once connected".
/// </summary>
public record DeleteCommunicationSyncConfigCommand(int Id) : IRequest<Unit>;

public class DeleteCommunicationSyncConfigHandler(AppDbContext db)
    : IRequestHandler<DeleteCommunicationSyncConfigCommand, Unit>
{
    public async Task<Unit> Handle(DeleteCommunicationSyncConfigCommand request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("DeleteCommunicationSyncConfig requires an authenticated caller.");

        var config = await db.CommunicationSyncConfigs
            .FirstOrDefaultAsync(c => c.Id == request.Id && c.UserId == userId, cancellationToken)
            ?? throw new KeyNotFoundException($"CommunicationSyncConfig {request.Id} not found for user {userId}");

        // Soft-delete. SetTimestamps on the next save will stamp DeletedBy
        // automatically since CurrentUserId is populated.
        config.DeletedAt = DateTimeOffset.UtcNow;

        db.LogActivityAt(
            "communication-sync-connection-removed",
            $"Removed {config.Kind.ToString().ToLowerInvariant()} sync connection: {config.ProviderId}"
                + (config.DisplayLabel is null ? string.Empty : $" ({config.DisplayLabel})"),
            ("User", userId));

        await db.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
