using FluentValidation;
using MediatR;

using Forge.Api.Features.Notifications;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.Approvals;

public record DelegateRequestCommand(int RequestId, int DecidedById, DelegateApprovalRequestModel Data) : IRequest<Unit>;

public class DelegateRequestValidator : AbstractValidator<DelegateRequestCommand>
{
    public DelegateRequestValidator()
    {
        RuleFor(x => x.Data.DelegateToUserId).GreaterThan(0);
    }
}

public class DelegateRequestHandler(IApprovalService approvalService, IAssignmentNotifier? assignmentNotifier = null)
    : IRequestHandler<DelegateRequestCommand, Unit>
{
    public async Task<Unit> Handle(DelegateRequestCommand request, CancellationToken ct)
    {
        await approvalService.EnsureCanActAsync(request.RequestId, request.DecidedById, ct);
        var delegated = await approvalService.DelegateAsync(
            request.RequestId, request.DecidedById,
            request.Data.DelegateToUserId, request.Data.Comments, ct);

        if (assignmentNotifier is not null)
        {
            await assignmentNotifier.ApprovalAssignedAsync(
                delegated.EntityType, delegated.EntityId, delegated.EntitySummary, [request.Data.DelegateToUserId], ct);
        }
        return Unit.Value;
    }
}
