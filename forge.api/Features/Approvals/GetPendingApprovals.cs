using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record GetPendingApprovalsQuery(int UserId) : IRequest<List<ApprovalRequestResponseModel>>;

public class GetPendingApprovalsHandler(IApprovalService approvalService, AppDbContext db)
    : IRequestHandler<GetPendingApprovalsQuery, List<ApprovalRequestResponseModel>>
{
    public async Task<List<ApprovalRequestResponseModel>> Handle(GetPendingApprovalsQuery request, CancellationToken ct)
    {
        var pending = await approvalService.GetPendingApprovalsAsync(request.UserId, ct);
        if (pending.Count == 0) return [];

        var allUserIds = pending
            .SelectMany(r => new[] { r.RequestedById }
                .Concat(r.Decisions.Select(d => d.DecidedById)))
            .Distinct()
            .ToList();

        var userNames = await db.Users.AsNoTracking()
            .Where(u => allUserIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);

        return pending.Select(r =>
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
