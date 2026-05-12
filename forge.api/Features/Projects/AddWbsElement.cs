using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record AddWbsElementCommand(int ProjectId, CreateWbsElementRequestModel Request) : IRequest<int>;

public class AddWbsElementHandler(AppDbContext db) : IRequestHandler<AddWbsElementCommand, int>
{
    public async Task<int> Handle(AddWbsElementCommand command, CancellationToken cancellationToken)
    {
        var projectExists = await db.Projects
            .AsNoTracking()
            .AnyAsync(p => p.Id == command.ProjectId, cancellationToken);

        if (!projectExists)
            throw new KeyNotFoundException($"Project {command.ProjectId} not found");

        var req = command.Request;
        var element = new WbsElement
        {
            ProjectId = command.ProjectId,
            ParentElementId = req.ParentElementId,
            Code = req.Code,
            Name = req.Name,
            Type = req.Type,
            BudgetLabor = req.BudgetLabor,
            BudgetMaterial = req.BudgetMaterial,
            BudgetOther = req.BudgetOther,
            BudgetTotal = req.BudgetLabor + req.BudgetMaterial + req.BudgetOther,
            PlannedStart = req.PlannedStart,
            PlannedEnd = req.PlannedEnd,
            SortOrder = req.SortOrder,
        };

        db.WbsElements.Add(element);
        await db.SaveChangesAsync(cancellationToken);

        return element.Id;
    }
}
