using FluentValidation;
using MediatR;

using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.TimeTracking;

public record RecomputeLaborAllocationCommand(LaborRecomputeRequestModel Data) : IRequest<LaborRecomputeResponseModel>;

public class RecomputeLaborAllocationValidator : AbstractValidator<RecomputeLaborAllocationCommand>
{
    public const int MaxDays = 62;

    public RecomputeLaborAllocationValidator()
    {
        RuleFor(x => x.Data.To).GreaterThanOrEqualTo(x => x.Data.From)
            .WithMessage("The end date must be on or after the start date.");
        RuleFor(x => x.Data)
            .Must(d => d.To.DayNumber - d.From.DayNumber < MaxDays)
            .WithMessage($"Recompute at most {MaxDays} days at a time.");
        RuleFor(x => x.Data.UserId).GreaterThan(0).When(x => x.Data.UserId.HasValue);
    }
}

public class RecomputeLaborAllocationHandler(ILaborAllocationService laborAllocation)
    : IRequestHandler<RecomputeLaborAllocationCommand, LaborRecomputeResponseModel>
{
    public Task<LaborRecomputeResponseModel> Handle(RecomputeLaborAllocationCommand request, CancellationToken cancellationToken)
        => laborAllocation.RecomputeAsync(
            request.Data.UserId, request.Data.From, request.Data.To, apply: !request.Data.Preview, cancellationToken);
}
