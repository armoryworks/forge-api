using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record RejectRequestCommand(int RequestId, int DecidedById, string Comments) : IRequest<ApprovalRequestResponseModel>;

public class RejectRequestValidator : AbstractValidator<RejectRequestCommand>
{
    public RejectRequestValidator()
    {
        RuleFor(x => x.Comments).NotEmpty();
    }
}

public class RejectRequestHandler(IApprovalService approvalService, AppDbContext db)
    : IRequestHandler<RejectRequestCommand, ApprovalRequestResponseModel>
{
    public async Task<ApprovalRequestResponseModel> Handle(RejectRequestCommand request, CancellationToken ct)
    {
        var result = await approvalService.RejectAsync(request.RequestId, request.DecidedById, request.Comments, ct);

        var r = await db.ApprovalRequests
            .AsNoTracking()
            .Include(x => x.Workflow).ThenInclude(w => w.Steps)
            .Include(x => x.Decisions)
            .FirstAsync(x => x.Id == result.Id, ct);

        var userIds = new List<int> { r.RequestedById };
        userIds.AddRange(r.Decisions.Select(d => d.DecidedById));

        var userNames = await db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => $"{u.LastName}, {u.FirstName}", ct);

        return new ApprovalRequestResponseModel(
            r.Id, r.Workflow.Name, r.EntityType, r.EntityId,
            r.EntitySummary, r.Amount,
            r.CurrentStepNumber,
            r.Workflow.Steps.FirstOrDefault(s => s.StepNumber == r.CurrentStepNumber)?.Name,
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
    }
}
