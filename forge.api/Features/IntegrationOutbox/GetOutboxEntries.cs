using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Data.Context;

namespace Forge.Api.Features.IntegrationOutbox;

public record GetOutboxEntriesQuery(
    OutboxStatus? Status = null,
    IntegrationProvider? Provider = null,
    int Take = 200) : IRequest<List<OutboxEntryResponseModel>>;

public class GetOutboxEntriesHandler(AppDbContext db)
    : IRequestHandler<GetOutboxEntriesQuery, List<OutboxEntryResponseModel>>
{
    public async Task<List<OutboxEntryResponseModel>> Handle(
        GetOutboxEntriesQuery request, CancellationToken ct)
    {
        var query = db.IntegrationOutboxEntries.AsNoTracking();

        if (request.Status.HasValue)
        {
            query = query.Where(e => e.Status == request.Status.Value);
        }

        if (request.Provider.HasValue)
        {
            query = query.Where(e => e.Provider == request.Provider.Value);
        }

        return await query
            .OrderByDescending(e => e.Id)
            .Take(Math.Min(request.Take, 1000))
            .Select(e => new OutboxEntryResponseModel(
                e.Id,
                e.Provider,
                e.OperationKey,
                e.Status,
                e.AttemptCount,
                e.MaxAttempts,
                e.NextAttemptAt,
                e.LastAttemptAt,
                e.SentAt,
                e.LastError,
                e.EntityType,
                e.EntityId,
                e.CreatedAt))
            .ToListAsync(ct);
    }
}
