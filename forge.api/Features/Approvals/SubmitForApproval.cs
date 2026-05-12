using System.Security.Claims;

using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

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

public class SubmitForApprovalHandler(IApprovalService approvalService)
    : IRequestHandler<SubmitForApprovalCommand, ApprovalRequestResponseModel?>
{
    public async Task<ApprovalRequestResponseModel?> Handle(SubmitForApprovalCommand request, CancellationToken ct)
    {
        var result = await approvalService.SubmitForApprovalAsync(
            request.Data.EntityType, request.Data.EntityId,
            request.RequestedById, request.Data.Amount,
            request.Data.EntitySummary, ct);

        if (result == null) return null;

        return new ApprovalRequestResponseModel(
            result.Id, result.Workflow?.Name ?? "", result.EntityType, result.EntityId,
            result.EntitySummary, result.Amount,
            result.CurrentStepNumber, null,
            result.Status.ToString(), "",
            result.RequestedAt, result.CompletedAt, []);
    }
}
