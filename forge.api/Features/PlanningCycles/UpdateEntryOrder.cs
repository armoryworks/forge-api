using FluentValidation;
using MediatR;
using Forge.Core.Interfaces;
using Forge.Core.Models;

namespace Forge.Api.Features.PlanningCycles;

public record UpdateEntryOrderCommand(int CycleId, List<EntryOrderItem> Items) : IRequest;

public class UpdateEntryOrderValidator : AbstractValidator<UpdateEntryOrderCommand>
{
    public UpdateEntryOrderValidator()
    {
        RuleFor(x => x.CycleId).GreaterThan(0);
        RuleFor(x => x.Items).NotEmpty().WithMessage("At least one entry is required");
        RuleForEach(x => x.Items).ChildRules(item =>
        {
            item.RuleFor(i => i.JobId).GreaterThan(0);
            item.RuleFor(i => i.SortOrder).GreaterThanOrEqualTo(0);
        });
    }
}

public class UpdateEntryOrderHandler(IPlanningCycleRepository repo)
    : IRequestHandler<UpdateEntryOrderCommand>
{
    public async Task Handle(UpdateEntryOrderCommand request, CancellationToken cancellationToken)
    {
        foreach (var item in request.Items)
        {
            var entry = await repo.FindEntryAsync(request.CycleId, item.JobId, cancellationToken);
            if (entry != null)
                entry.SortOrder = item.SortOrder;
        }

        await repo.SaveChangesAsync(cancellationToken);
    }
}
