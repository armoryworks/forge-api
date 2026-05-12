using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record GetApprovalWorkflowsQuery : IRequest<List<ApprovalWorkflowResponseModel>>;

public class GetApprovalWorkflowsHandler(AppDbContext db)
    : IRequestHandler<GetApprovalWorkflowsQuery, List<ApprovalWorkflowResponseModel>>
{
    public async Task<List<ApprovalWorkflowResponseModel>> Handle(GetApprovalWorkflowsQuery request, CancellationToken ct)
    {
        var workflows = await db.ApprovalWorkflows
            .AsNoTracking()
            .Include(w => w.Steps)
            .OrderBy(w => w.EntityType)
            .ThenBy(w => w.Name)
            .ToListAsync(ct);

        var approverUserIds = workflows
            .SelectMany(w => w.Steps)
            .Where(s => s.ApproverUserId.HasValue)
            .Select(s => s.ApproverUserId!.Value)
            .Distinct()
            .ToList();

        var userNames = approverUserIds.Count > 0
            ? await db.Users.AsNoTracking()
                .Where(u => approverUserIds.Contains(u.Id))
                .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct)
            : new Dictionary<int, string>();

        return workflows.Select(w => new ApprovalWorkflowResponseModel(
            w.Id, w.Name, w.EntityType, w.IsActive,
            w.Description, w.ActivationConditionsJson,
            w.Steps.OrderBy(s => s.StepNumber).Select(s => new ApprovalStepResponseModel(
                s.Id, s.StepNumber, s.Name, s.ApproverType.ToString(),
                s.ApproverUserId,
                s.ApproverUserId.HasValue && userNames.TryGetValue(s.ApproverUserId.Value, out var name) ? name : null,
                s.ApproverRole, s.UseDirectManager, s.AutoApproveBelow,
                s.EscalationHours, s.RequireComments, s.AllowDelegation
            )).ToList(),
            w.CreatedAt
        )).ToList();
    }
}
