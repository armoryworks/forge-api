using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs.Operations;

public record GetJobOperationEventsQuery(int JobId, int OperationId) : IRequest<IReadOnlyList<JobOperationEventResponseModel>>;

public class GetJobOperationEventsHandler(AppDbContext db)
    : IRequestHandler<GetJobOperationEventsQuery, IReadOnlyList<JobOperationEventResponseModel>>
{
    public async Task<IReadOnlyList<JobOperationEventResponseModel>> Handle(
        GetJobOperationEventsQuery request, CancellationToken ct)
    {
        if (!await db.Jobs.AnyAsync(j => j.Id == request.JobId, ct))
            throw new KeyNotFoundException($"Job {request.JobId} not found");

        var events = await db.JobOperationEvents
            .AsNoTracking()
            .Where(e => e.JobOperation.JobId == request.JobId && e.JobOperation.OperationId == request.OperationId)
            .OrderBy(e => e.OccurredAt)
            .ThenBy(e => e.Id)
            .Select(e => new { e.Id, e.Kind, e.Quantity, e.ReasonCode, e.UserId, e.OccurredAt })
            .ToListAsync(ct);

        var userIds = events.Select(e => e.UserId).Distinct().ToList();
        var names = await db.Users
            .AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.LastName + ", " + u.FirstName, ct);

        return events
            .Select(e => new JobOperationEventResponseModel(
                e.Id, e.Kind, e.Quantity, e.ReasonCode, e.UserId, names.GetValueOrDefault(e.UserId, "Unknown"), e.OccurredAt))
            .ToList();
    }
}
