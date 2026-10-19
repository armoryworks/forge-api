using MediatR;

using Forge.Core.Interfaces;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record RecalculateProjectTotalsCommand(int Id) : IRequest;

public class RecalculateProjectTotalsHandler(AppDbContext db, IClock clock) : IRequestHandler<RecalculateProjectTotalsCommand>
{
    public async Task Handle(RecalculateProjectTotalsCommand command, CancellationToken cancellationToken)
    {
        var rollup = await ProjectRollup.LoadAsync(db, clock, command.Id, track: true, cancellationToken);

        foreach (var element in rollup.Elements)
        {
            var (labor, material, other) = rollup.ActualsFor(element.Id);
            element.ActualLabor = labor;
            element.ActualMaterial = material;
            element.ActualOther = other;
            element.ActualTotal = labor + material + other;
        }

        var earned = rollup.EarnedValue();
        rollup.Project.ActualTotal = earned.ActualCostOfWorkPerformed;
        rollup.Project.EstimateAtCompletionTotal = earned.EstimateAtCompletion;
        if (rollup.Elements.Any(e => e.PercentComplete.HasValue))
            rollup.Project.PercentComplete = Math.Round(rollup.PercentComplete, 2);

        await db.SaveChangesAsync(cancellationToken);
    }
}
