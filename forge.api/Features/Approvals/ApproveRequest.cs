using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record ApproveRequestCommand(int RequestId, int DecidedById, string? Comments) : IRequest<ApprovalRequestResponseModel>;

public class ApproveRequestHandler(IApprovalService approvalService, AppDbContext db)
    : IRequestHandler<ApproveRequestCommand, ApprovalRequestResponseModel>
{
    public async Task<ApprovalRequestResponseModel> Handle(ApproveRequestCommand request, CancellationToken ct)
    {
        var result = await approvalService.ApproveAsync(request.RequestId, request.DecidedById, request.Comments, ct);
        return await MapToResponseAsync(result.Id, ct);
    }

    private async Task<ApprovalRequestResponseModel> MapToResponseAsync(int requestId, CancellationToken ct)
    {
        var r = await db.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.Workflow).ThenInclude(w => w.Steps)
            .Include(x => x.Decisions)
            .FirstAsync(x => x.Id == requestId, ct);

        var userIds = new List<int> { r.RequestedById };
        userIds.AddRange(r.Decisions.Select(d => d.DecidedById));
        userIds.AddRange(r.Decisions.Where(d => d.DelegatedToUserId.HasValue).Select(d => d.DelegatedToUserId!.Value));

        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);

        var currentStep = r.Workflow.Steps.FirstOrDefault(s => s.StepNumber == r.CurrentStepNumber);

        return new ApprovalRequestResponseModel(
            r.Id, r.Workflow.Name, r.EntityType, r.EntityId,
            r.EntitySummary, r.Amount,
            r.CurrentStepNumber, currentStep?.Name,
            r.Status.ToString(),
            userNames.GetValueOrDefault(r.RequestedById, ""),
            r.RequestedAt, r.CompletedAt,
            r.Decisions.OrderBy(d => d.DecidedAt).Select(d =>
            {
                var stepName = r.Workflow.Steps.FirstOrDefault(s => s.StepNumber == d.StepNumber)?.Name ?? "";
                return new ApprovalDecisionResponseModel(
                    d.Id, d.StepNumber, stepName,
                    userNames.GetValueOrDefault(d.DecidedById, ""),
                    d.Decision.ToString(), d.Comments, d.DecidedAt,
                    d.DelegatedToUserId.HasValue ? userNames.GetValueOrDefault(d.DelegatedToUserId.Value, "") : null);
            }).ToList()
        );
    }
}
