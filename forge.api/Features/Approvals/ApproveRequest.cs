using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.DomainEvents;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record ApproveRequestCommand(int RequestId, int DecidedById, string? Comments) : IRequest<ApprovalRequestResponseModel>;

public class ApproveRequestHandler(IApprovalService approvalService, AppDbContext db, IMediator mediator)
    : IRequestHandler<ApproveRequestCommand, ApprovalRequestResponseModel>
{
    public async Task<ApprovalRequestResponseModel> Handle(ApproveRequestCommand request, CancellationToken ct)
    {
        // ApproveAsync advances the step and SaveChanges; on the FINAL step it sets the
        // terminal Status (Approved / AutoApproved) + CompletedAt. The terminal status is
        // therefore already persisted by the time it returns — so a downstream re-query in
        // any ApprovalCompletedEvent handler sees the request as terminal (the guard in
        // UpdateExpenseStatus relies on this ordering: publish AFTER persist).
        var result = await approvalService.ApproveAsync(request.RequestId, request.DecidedById, request.Comments, ct);

        // F-26B-05 — fire the completion event ONLY on a terminal approval (final step done),
        // never on an intermediate step advance (where Status is still Pending/Escalated).
        if (result.Status is ApprovalRequestStatus.Approved or ApprovalRequestStatus.AutoApproved)
        {
            await mediator.Publish(
                new ApprovalCompletedEvent(
                    result.EntityType, result.EntityId,
                    Approved: true, request.DecidedById, request.Comments),
                ct);
        }

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
