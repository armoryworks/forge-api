using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ReturnMaterialIssueCommand(int JobId, int IssueId, int ReturnedById)
    : IRequest<MaterialIssueResponseModel>;

public class ReturnMaterialIssueHandler(AppDbContext db)
    : IRequestHandler<ReturnMaterialIssueCommand, MaterialIssueResponseModel>
{
    public async Task<MaterialIssueResponseModel> Handle(
        ReturnMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        var original = await db.MaterialIssues
            .Include(m => m.Part)
            .FirstOrDefaultAsync(m => m.Id == request.IssueId && m.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"MaterialIssue {request.IssueId} not found for job {request.JobId}");

        if (original.IssueType != MaterialIssueType.Issue)
            throw new InvalidOperationException("Only issued materials can be returned");

        // Create a return record
        var returnIssue = new MaterialIssue
        {
            JobId = original.JobId,
            PartId = original.PartId,
            OperationId = original.OperationId,
            Quantity = original.Quantity,
            UnitCost = original.UnitCost,
            IssuedById = request.ReturnedById,
            IssuedAt = DateTimeOffset.UtcNow,
            BinContentId = original.BinContentId,
            StorageLocationId = original.StorageLocationId,
            LotNumber = original.LotNumber,
            IssueType = MaterialIssueType.Return,
            ReturnReasonId = null,
            Notes = $"Return of issue #{original.Id}",
        };

        db.MaterialIssues.Add(returnIssue);

        // Restore bin quantity
        if (original.BinContentId.HasValue)
        {
            var bin = await db.BinContents.FindAsync([original.BinContentId.Value], cancellationToken);
            if (bin != null)
            {
                bin.Quantity += original.Quantity;

                db.BinMovements.Add(new BinMovement
                {
                    EntityType = "part",
                    EntityId = original.PartId,
                    Quantity = original.Quantity,
                    ToLocationId = bin.LocationId,
                    Reason = BinMovementReason.Adjustment,
                    MovedBy = request.ReturnedById,
                    MovedAt = DateTimeOffset.UtcNow,
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new MaterialIssueResponseModel
        {
            Id = returnIssue.Id,
            JobId = returnIssue.JobId,
            PartId = returnIssue.PartId,
            PartNumber = original.Part.PartNumber,
            PartDescription = original.Part.Description ?? string.Empty,
            OperationId = returnIssue.OperationId,
            Quantity = returnIssue.Quantity,
            UnitCost = returnIssue.UnitCost,
            TotalCost = returnIssue.Quantity * returnIssue.UnitCost,
            IssuedAt = returnIssue.IssuedAt,
            LotNumber = returnIssue.LotNumber,
            IssueType = returnIssue.IssueType,
            Notes = returnIssue.Notes,
        };
    }
}
