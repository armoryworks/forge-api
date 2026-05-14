using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record GetCapaTasksQuery(int CapaId) : IRequest<List<CapaTaskResponseModel>>;

public class GetCapaTasksHandler(AppDbContext db)
    : IRequestHandler<GetCapaTasksQuery, List<CapaTaskResponseModel>>
{
    public async Task<List<CapaTaskResponseModel>> Handle(
        GetCapaTasksQuery request, CancellationToken cancellationToken)
    {
        var tasks = await db.CapaTasks
            .AsNoTracking()
            .Where(t => t.CapaId == request.CapaId)
            .OrderBy(t => t.SortOrder)
            .ToListAsync(cancellationToken);

        var userIds = tasks
            .SelectMany(t => new[] { t.AssigneeId, t.CompletedById ?? 0 })
            .Where(id => id > 0)
            .Distinct()
            .ToList();

        var userNames = await db.Users
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", cancellationToken);

        return tasks.Select(t => new CapaTaskResponseModel
        {
            Id = t.Id,
            CapaId = t.CapaId,
            Title = t.Title,
            Description = t.Description,
            AssigneeId = t.AssigneeId,
            AssigneeName = userNames.GetValueOrDefault(t.AssigneeId, "Unknown"),
            DueDate = t.DueDate,
            Status = t.Status,
            CompletedAt = t.CompletedAt,
            CompletedById = t.CompletedById,
            CompletedByName = t.CompletedById.HasValue ? userNames.GetValueOrDefault(t.CompletedById.Value) : null,
            CompletionNotes = t.CompletionNotes,
            SortOrder = t.SortOrder,
        }).ToList();
    }
}
