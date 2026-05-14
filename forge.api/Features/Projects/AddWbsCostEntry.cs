using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Projects;

public record AddWbsCostEntryCommand(int ProjectId, int ElementId, CreateWbsCostEntryRequestModel Request) : IRequest;

public class AddWbsCostEntryHandler(AppDbContext db, IClock clock) : IRequestHandler<AddWbsCostEntryCommand>
{
    public async Task Handle(AddWbsCostEntryCommand command, CancellationToken cancellationToken)
    {
        var element = await db.WbsElements
            .FirstOrDefaultAsync(e => e.Id == command.ElementId && e.ProjectId == command.ProjectId, cancellationToken)
            ?? throw new KeyNotFoundException($"WBS element {command.ElementId} not found in project {command.ProjectId}");

        var req = command.Request;
        var entry = new WbsCostEntry
        {
            WbsElementId = command.ElementId,
            Category = req.Category,
            Amount = req.Amount,
            Description = req.Description,
            SourceEntityType = req.SourceEntityType,
            SourceEntityId = req.SourceEntityId,
            EntryDate = req.EntryDate ?? clock.UtcNow,
        };

        db.WbsCostEntries.Add(entry);

        // Update element actuals
        switch (req.Category)
        {
            case WbsCostCategory.Labor:
                element.ActualLabor += req.Amount;
                break;
            case WbsCostCategory.Material:
                element.ActualMaterial += req.Amount;
                break;
            default:
                element.ActualOther += req.Amount;
                break;
        }
        element.ActualTotal = element.ActualLabor + element.ActualMaterial + element.ActualOther;

        await db.SaveChangesAsync(cancellationToken);
    }
}
