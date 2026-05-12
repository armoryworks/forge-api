using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models.Communications;
using Forge.Data.Context;

namespace Forge.Api.Features.Communications;

/// <summary>
/// Wave 8 — list the calling user's communication-sync connections.
/// Drives the user-settings "Connected mailboxes / phones" panel.
/// Tokens are filtered out of the projection (sealed envelopes belong
/// to the service tier, not the UI).
/// </summary>
public record GetCommunicationSyncConfigsQuery() : IRequest<List<CommunicationSyncConfigResponseModel>>;

public class GetCommunicationSyncConfigsHandler(AppDbContext db)
    : IRequestHandler<GetCommunicationSyncConfigsQuery, List<CommunicationSyncConfigResponseModel>>
{
    public async Task<List<CommunicationSyncConfigResponseModel>> Handle(
        GetCommunicationSyncConfigsQuery request, CancellationToken cancellationToken)
    {
        var userId = db.CurrentUserId
            ?? throw new InvalidOperationException("GetCommunicationSyncConfigs requires an authenticated caller.");

        return await db.CommunicationSyncConfigs.AsNoTracking()
            .Where(c => c.UserId == userId)
            .OrderBy(c => c.Kind).ThenBy(c => c.ProviderId)
            .Select(c => new CommunicationSyncConfigResponseModel(
                c.Id, c.UserId, c.Kind, c.ProviderId, c.DisplayLabel,
                c.IsConnected, c.ExternalAccountId, c.LastSyncedAt,
                c.LastError, c.LastErrorAt,
                c.CreatedAt, c.UpdatedAt))
            .ToListAsync(cancellationToken);
    }
}
