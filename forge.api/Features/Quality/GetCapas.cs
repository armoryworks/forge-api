using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetCapasQuery(
    CapaStatus? Status,
    CapaType? Type,
    int? OwnerId,
    int? Priority,
    DateTimeOffset? DueDateFrom,
    DateTimeOffset? DueDateTo
) : IRequest<List<CapaResponseModel>>;

public class GetCapasHandler(AppDbContext db)
    : IRequestHandler<GetCapasQuery, List<CapaResponseModel>>
{
    public async Task<List<CapaResponseModel>> Handle(
        GetCapasQuery request, CancellationToken cancellationToken)
    {
        var query = db.CorrectiveActions
            .AsNoTracking()
            .Include(c => c.Tasks)
            .Include(c => c.RelatedNcrs)
            .AsQueryable();

        if (request.Status.HasValue)
            query = query.Where(c => c.Status == request.Status.Value);
        if (request.Type.HasValue)
            query = query.Where(c => c.Type == request.Type.Value);
        if (request.OwnerId.HasValue)
            query = query.Where(c => c.OwnerId == request.OwnerId.Value);
        if (request.Priority.HasValue)
            query = query.Where(c => c.Priority == request.Priority.Value);
        if (request.DueDateFrom.HasValue)
            query = query.Where(c => c.DueDate >= request.DueDateFrom.Value);
        if (request.DueDateTo.HasValue)
            query = query.Where(c => c.DueDate <= request.DueDateTo.Value);

        var capas = await query
            .OrderByDescending(c => c.Priority)
            .ThenBy(c => c.DueDate)
            .ToListAsync(cancellationToken);

        var userIds = capas
            .SelectMany(c => new[] { c.OwnerId, c.ClosedById ?? 0 })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return capas.Select(c => new CapaResponseModel
        {
            Id = c.Id,
            CapaNumber = c.CapaNumber,
            Type = c.Type,
            SourceType = c.SourceType,
            SourceEntityId = c.SourceEntityId,
            SourceEntityType = c.SourceEntityType,
            Title = c.Title,
            ProblemDescription = c.ProblemDescription,
            ImpactDescription = c.ImpactDescription,
            OwnerId = c.OwnerId,
            OwnerName = userNames.GetValueOrDefault(c.OwnerId, "Unknown"),
            Status = c.Status,
            Priority = c.Priority,
            DueDate = c.DueDate,
            ClosedAt = c.ClosedAt,
            ClosedById = c.ClosedById,
            ClosedByName = c.ClosedById.HasValue ? userNames.GetValueOrDefault(c.ClosedById.Value) : null,
            TaskCount = c.Tasks.Count,
            CompletedTaskCount = c.Tasks.Count(t => t.Status == CapaTaskStatus.Completed),
            RelatedNcrCount = c.RelatedNcrs.Count,
            CreatedAt = c.CreatedAt,
        }).ToList();
    }
}
