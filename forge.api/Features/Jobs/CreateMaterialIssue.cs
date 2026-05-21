using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record CreateMaterialIssueCommand(
    int JobId,
    int PartId,
    int? OperationId,
    decimal Quantity,
    int? BinContentId,
    int? StorageLocationId,
    string? LotNumber,
    MaterialIssueType IssueType,
    string? Notes,
    int IssuedById) : IRequest<MaterialIssueResponseModel>;

public class CreateMaterialIssueValidator : AbstractValidator<CreateMaterialIssueCommand>
{
    public CreateMaterialIssueValidator()
    {
        RuleFor(x => x.PartId).GreaterThan(0);
        RuleFor(x => x.Quantity).GreaterThan(0);
    }
}

public class CreateMaterialIssueHandler(AppDbContext db)
    : IRequestHandler<CreateMaterialIssueCommand, MaterialIssueResponseModel>
{
    public async Task<MaterialIssueResponseModel> Handle(
        CreateMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        // INV-SF2 / F-033: source-state guard — archived jobs are closed to material transactions
        var job = await db.Jobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found");

        if (job.IsArchived)
            throw new InvalidOperationException(
                $"Cannot issue material to archived job {request.JobId}.");

        // Get unit cost from last PO line for this part, or zero
        var unitCost = await db.PurchaseOrderLines
            .AsNoTracking()
            .Where(pol => pol.PartId == request.PartId)
            .OrderByDescending(pol => pol.Id)
            .Select(pol => pol.UnitPrice)
            .FirstOrDefaultAsync(cancellationToken);

        if (request.BinContentId.HasValue)
        {
            var bin = await db.BinContents.FindAsync([request.BinContentId.Value], cancellationToken)
                ?? throw new KeyNotFoundException($"BinContent {request.BinContentId} not found");

            // Decrement bin quantity for Issue, increment for Return
            if (request.IssueType == MaterialIssueType.Issue || request.IssueType == MaterialIssueType.Scrap)
            {
                if (bin.Quantity < request.Quantity)
                    throw new InvalidOperationException($"Insufficient quantity in bin. Available: {bin.Quantity}, Requested: {request.Quantity}");
                bin.Quantity -= request.Quantity;
            }
            else if (request.IssueType == MaterialIssueType.Return)
            {
                bin.Quantity += request.Quantity;
            }

            // Record bin movement
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = request.PartId,
                Quantity = request.IssueType == MaterialIssueType.Return ? request.Quantity : -request.Quantity,
                FromLocationId = bin.LocationId,
                Reason = BinMovementReason.Adjustment,
                MovedBy = request.IssuedById,
                MovedAt = DateTimeOffset.UtcNow,
            });
        }

        var issue = new MaterialIssue
        {
            JobId = request.JobId,
            PartId = request.PartId,
            OperationId = request.OperationId,
            Quantity = request.Quantity,
            UnitCost = unitCost,
            IssuedById = request.IssuedById,
            IssuedAt = DateTimeOffset.UtcNow,
            BinContentId = request.BinContentId,
            StorageLocationId = request.StorageLocationId,
            LotNumber = request.LotNumber,
            IssueType = request.IssueType,
            Notes = request.Notes,
        };

        db.MaterialIssues.Add(issue);
        await db.SaveChangesAsync(cancellationToken);

        var part = await db.Parts.AsNoTracking()
            .FirstAsync(p => p.Id == request.PartId, cancellationToken);

        return new MaterialIssueResponseModel
        {
            Id = issue.Id,
            JobId = issue.JobId,
            PartId = issue.PartId,
            PartNumber = part.PartNumber,
            PartDescription = part.Description ?? string.Empty,
            OperationId = issue.OperationId,
            Quantity = issue.Quantity,
            UnitCost = issue.UnitCost,
            TotalCost = issue.Quantity * issue.UnitCost,
            IssuedAt = issue.IssuedAt,
            LotNumber = issue.LotNumber,
            IssueType = issue.IssueType,
            Notes = issue.Notes,
        };
    }
}
