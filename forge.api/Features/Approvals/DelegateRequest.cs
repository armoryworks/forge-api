using FluentValidation;
using MediatR;

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

public class DelegateRequestHandler(IApprovalService approvalService)
    : IRequestHandler<DelegateRequestCommand, Unit>
{
    public async Task<Unit> Handle(DelegateRequestCommand request, CancellationToken ct)
    {
        await approvalService.DelegateAsync(
            request.RequestId, request.DecidedById,
            request.Data.DelegateToUserId, request.Data.Comments, ct);
        return Unit.Value;
    }
}
