using MediatR;

using Microsoft.EntityFrameworkCore;

using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.FollowUpTasks;

public record GetFollowUpTasksQuery(int UserId, FollowUpStatus? Status) : IRequest<List<FollowUpTaskResponseModel>>;

public class GetFollowUpTasksHandler(AppDbContext db) : IRequestHandler<GetFollowUpTasksQuery, List<FollowUpTaskResponseModel>>
{
    public async Task<List<FollowUpTaskResponseModel>> Handle(GetFollowUpTasksQuery request, CancellationToken ct)
    {
        var query = db.FollowUpTasks
            .Where(f => f.AssignedToUserId == request.UserId);

        if (request.Status.HasValue)
            query = query.Where(f => f.Status == request.Status.Value);

        var tasks = await query
            .Join(db.Users, f => f.AssignedToUserId, u => u.Id, (f, u) => new { Task = f, User = u })
            .OrderBy(x => x.Task.DueDate)
            .ThenByDescending(x => x.Task.CreatedAt)
            .Select(x => new FollowUpTaskResponseModel(
                x.Task.Id,
                x.Task.Title,
                x.Task.Description,
                x.Task.AssignedToUserId,
                x.User.LastName + ", " + x.User.FirstName,
                x.Task.DueDate,
                x.Task.SourceEntityType,
                x.Task.SourceEntityId,
                null,
                x.Task.TriggerType,
                x.Task.Status,
                x.Task.CompletedAt,
                x.Task.DismissedAt,
                x.Task.CreatedAt))
            .ToListAsync(ct);

        var labels = await ResolveLabelsAsync(tasks, ct);

        return tasks
            .Select(t => t with
            {
                SourceEntityLabel = t.SourceEntityType is string type && t.SourceEntityId is int id
                    ? labels.GetValueOrDefault((type, id))
                    : null,
            })
            .ToList();
    }

    private async Task<Dictionary<(string Type, int Id), string>> ResolveLabelsAsync(
        List<FollowUpTaskResponseModel> tasks, CancellationToken ct)
    {
        var idsByType = tasks
            .Where(t => t.SourceEntityType is not null && t.SourceEntityId is not null)
            .GroupBy(t => t.SourceEntityType!)
            .ToDictionary(g => g.Key, g => g.Select(t => t.SourceEntityId!.Value).Distinct().ToList());

        var labels = new Dictionary<(string Type, int Id), string>();

        async Task AddAsync(string type, Func<List<int>, Task<Dictionary<int, string>>> load)
        {
            if (!idsByType.TryGetValue(type, out var ids))
                return;

            foreach (var (id, label) in await load(ids))
                labels[(type, id)] = label;
        }

        await AddAsync("Job", ids => db.Jobs
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.JobNumber, ct));
        await AddAsync("PurchaseOrder", ids => db.PurchaseOrders
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.PONumber, ct));
        await AddAsync("Invoice", ids => db.Invoices
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.InvoiceNumber, ct));
        await AddAsync("Part", ids => db.Parts
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.PartNumber, ct));
        await AddAsync("CustomerReturn", ids => db.CustomerReturns
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.ReturnNumber, ct));
        await AddAsync("ReorderSuggestion", ids => db.ReorderSuggestions
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, e => e.Part.PartNumber, ct));

        return labels;
    }
}
