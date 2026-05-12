using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record UpdateWbsElementCommand(int ProjectId, int ElementId, UpdateWbsElementRequestModel Request) : IRequest;

public class UpdateWbsElementHandler(AppDbContext db) : IRequestHandler<UpdateWbsElementCommand>
{
    public async Task Handle(UpdateWbsElementCommand command, CancellationToken cancellationToken)
    {
        var element = await db.WbsElements
            .FirstOrDefaultAsync(e => e.Id == command.ElementId && e.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"WBS element {command.ElementId} not found in project {command.ProjectId}");

        var req = command.Request;
        if (req.Code != null) element.Code = req.Code;
        if (req.Name != null) element.Name = req.Name;
        if (req.Type.HasValue) element.Type = req.Type.Value;
        if (req.BudgetLabor.HasValue) element.BudgetLabor = req.BudgetLabor.Value;
        if (req.BudgetMaterial.HasValue) element.BudgetMaterial = req.BudgetMaterial.Value;
        if (req.BudgetOther.HasValue) element.BudgetOther = req.BudgetOther.Value;
        if (req.BudgetLabor.HasValue || req.BudgetMaterial.HasValue || req.BudgetOther.HasValue)
            element.BudgetTotal = element.BudgetLabor + element.BudgetMaterial + element.BudgetOther;
        if (req.PlannedStart.HasValue) element.PlannedStart = req.PlannedStart;
        if (req.PlannedEnd.HasValue) element.PlannedEnd = req.PlannedEnd;
        if (req.PercentComplete.HasValue) element.PercentComplete = req.PercentComplete;
        if (req.SortOrder.HasValue) element.SortOrder = req.SortOrder.Value;

        await db.SaveChangesAsync(cancellationToken);
    }
}
