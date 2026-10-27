using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Notifications;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Approvals;

public record SubmitForApprovalCommand(SubmitApprovalRequestModel Data, int RequestedById) : IRequest<ApprovalRequestResponseModel?>;

public class SubmitForApprovalValidator : AbstractValidator<SubmitForApprovalCommand>
{
    public SubmitForApprovalValidator()
    {
        RuleFor(x => x.Data.EntityType).NotEmpty();
        RuleFor(x => x.Data.EntityId).GreaterThan(0);
    }
}

public class SubmitForApprovalHandler(
    IApprovalService approvalService, IPublisher publisher, AppDbContext db, IAssignmentNotifier? assignmentNotifier = null)
    : IRequestHandler<SubmitForApprovalCommand, ApprovalRequestResponseModel?>
{
    public async Task<ApprovalRequestResponseModel?> Handle(SubmitForApprovalCommand request, CancellationToken ct)
    {
        var inProgressId = await db.ApprovalRequests
            .AsNoTracking()
            .Where(r => r.EntityType == request.Data.EntityType
                && r.EntityId == request.Data.EntityId
                && (r.Status == ApprovalRequestStatus.Pending || r.Status == ApprovalRequestStatus.Escalated))
            .Select(r => (int?)r.Id)
            .FirstOrDefaultAsync(ct);
        if (inProgressId is int existingId)
            throw new ApprovalAlreadyPendingException(request.Data.EntityType, request.Data.EntityId, existingId);

        var result = await approvalService.SubmitAndPublishAsync(
            publisher, request.Data.EntityType, request.Data.EntityId,
            request.RequestedById, request.Data.Amount,
            request.Data.EntitySummary, ct, assignmentNotifier);

        if (result == null) return null;

        return new ApprovalRequestResponseModel(
            result.Id, result.Workflow?.Name ?? "", result.EntityType, result.EntityId,
            result.EntitySummary, result.Amount,
            result.CurrentStepNumber, null,
            result.Status.ToString(), "",
            result.RequestedAt, result.CompletedAt, []);
    }
}
