using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;

namespace Forge.Api.Features.PlanningCycles;

public record UpdatePlanningCycleCommand(
    int Id,
    string? Name,
    DateTimeOffset? StartDate,
    DateTimeOffset? EndDate,
    string? Goals) : IRequest;

public class UpdatePlanningCycleValidator : AbstractValidator<UpdatePlanningCycleCommand>
{
    public UpdatePlanningCycleValidator()
    {
        RuleFor(x => x.Name).MaximumLength(200).When(x => x.Name != null);
        RuleFor(x => x.Goals).MaximumLength(2000).When(x => x.Goals != null);
    }
}

public class UpdatePlanningCycleHandler(IPlanningCycleRepository repo)
    : IRequestHandler<UpdatePlanningCycleCommand>
{
    public async Task Handle(UpdatePlanningCycleCommand request, CancellationToken cancellationToken)
    {
        var cycle = await repo.FindAsync(request.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"Planning cycle {request.Id} not found");

        if (cycle.Status == PlanningCycleStatus.Completed)
            throw new InvalidOperationException("Cannot update a completed planning cycle");

        if (request.Name != null) cycle.Name = request.Name;
        if (request.StartDate.HasValue) cycle.StartDate = request.StartDate.Value;
        if (request.EndDate.HasValue) cycle.EndDate = request.EndDate.Value;
        if (request.Goals != null) cycle.Goals = request.Goals;

        await repo.SaveChangesAsync(cancellationToken);
    }
}
