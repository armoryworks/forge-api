using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Quality;

public record UpdateNcrCommand(int Id, UpdateNcrRequestModel Request) : IRequest;

public class UpdateNcrHandler(AppDbContext db)
    : IRequestHandler<UpdateNcrCommand>
{
    public async Task Handle(UpdateNcrCommand command, CancellationToken cancellationToken)
    {
        var ncr = await db.NonConformances
            .FirstOrDefaultAsync(n => n.Id == command.Id, cancellationToken)
            ?? throw new KeyNotFoundException($"NCR {command.Id} not found");

        var req = command.Request;

        if (req.Type.HasValue) ncr.Type = req.Type.Value;
        if (req.Description != null) ncr.Description = req.Description;
        if (req.AffectedQuantity.HasValue) ncr.AffectedQuantity = req.AffectedQuantity.Value;
        if (req.DefectiveQuantity.HasValue) ncr.DefectiveQuantity = req.DefectiveQuantity.Value;
        if (req.ContainmentActions != null) ncr.ContainmentActions = req.ContainmentActions;
        if (req.MaterialCost.HasValue) ncr.MaterialCost = req.MaterialCost.Value;
        if (req.LaborCost.HasValue) ncr.LaborCost = req.LaborCost.Value;
        if (req.Status.HasValue) ncr.Status = req.Status.Value;

        // Auto-compute total cost
        if (req.MaterialCost.HasValue || req.LaborCost.HasValue)
            ncr.TotalCostImpact = (ncr.MaterialCost ?? 0) + (ncr.LaborCost ?? 0);

        await db.SaveChangesAsync(cancellationToken);
    }
}
