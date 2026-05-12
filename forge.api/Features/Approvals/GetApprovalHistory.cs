using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record GetApprovalHistoryQuery(string EntityType, int EntityId) : IRequest<List<ApprovalRequestResponseModel>>;

public class GetApprovalHistoryHandler(AppDbContext db)
    : IRequestHandler<GetApprovalHistoryQuery, List<ApprovalRequestResponseModel>>
{
    public async Task<List<ApprovalRequestResponseModel>> Handle(GetApprovalHistoryQuery request, CancellationToken ct)
    {
        var requests = await db.ApprovalRequests
            .AsNoTracking()
            .Include(r => r.Workflow).ThenInclude(w => w.Steps)
            .Include(r => r.Decisions)
            .Where(r => r.EntityType == request.EntityType && r.EntityId == request.EntityId)
            .OrderByDescending(r => r.RequestedAt)
            .ToListAsync(ct);

        if (requests.Count == 0) return [];

        var allUserIds = requests
            .SelectMany(r => new[] { r.RequestedById }
                .Concat(r.Decisions.Select(d => d.DecidedById)))
            .Distinct()
            .ToList();

        var userNames = await db.Users.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);

        return requests.Select(r =>
        {
            var currentStep = r.Workflow.Steps.FirstOrDefault(s => s.StepNumber == r.CurrentStepNumber);
            return new ApprovalRequestResponseModel(
                r.Id, r.Workflow.Name, r.EntityType, r.EntityId,
                r.EntitySummary, r.Amount,
                r.CurrentStepNumber, currentStep?.Name,
                r.Status.ToString(),
                userNames.GetValueOrDefault(r.RequestedById, ""),
                r.RequestedAt, r.CompletedAt,
                r.Decisions.OrderBy(d => d.DecidedAt).Select(d => new ApprovalDecisionResponseModel(
                    d.Id, d.StepNumber,
                    r.Workflow.Steps.FirstOrDefault(s => s.StepNumber == d.StepNumber)?.Name ?? "",
                    userNames.GetValueOrDefault(d.DecidedById, ""),
                    d.Decision.ToString(), d.Comments, d.DecidedAt, null
                )).ToList()
            );
        }).ToList();
    }
}
