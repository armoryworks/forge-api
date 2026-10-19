using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
using Forge.Api.Features.Quality;
using Forge.Api.Features.Scanner;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ReturnMaterialIssueCommand(int JobId, int IssueId, int ReturnedById, bool StockRestoredByCaller = false)
    : IRequest<MaterialIssueResponseModel>;

public class ReturnMaterialIssueHandler(AppDbContext db, IMaterialIssuePostingService? posting = null)
    : IRequestHandler<ReturnMaterialIssueCommand, MaterialIssueResponseModel>
{
    public async Task<MaterialIssueResponseModel> Handle(
        ReturnMaterialIssueCommand request, CancellationToken cancellationToken)
    {
        await using var tx = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM material_issues WHERE id = {request.IssueId} FOR UPDATE", cancellationToken);

        var original = await db.MaterialIssues
            .Include(m => m.Part)
            .FirstOrDefaultAsync(m => m.Id == request.IssueId && m.JobId == request.JobId, cancellationToken)
            ?? throw new KeyNotFoundException($"MaterialIssue {request.IssueId} not found for job {request.JobId}");

        // INV-SF2 / F-033: source-state guard — archived jobs are closed to material transactions
        var job = await db.Jobs.AsNoTracking()
            .FirstOrDefaultAsync(j => j.Id == request.JobId, cancellationToken);
        if (job?.IsArchived == true)
            throw new InvalidOperationException(
                $"Cannot return material to archived job {request.JobId}.");

        if (original.IssueType != MaterialIssueType.Issue)
            throw new InvalidOperationException("Only issued materials can be returned");

        if (await MaterialIssueReturn.AnyReturnedAsync(db, original.JobId, [original.Id], cancellationToken))
            throw new InvalidOperationException($"Issue #{original.Id} has already been returned.");

        var now = DateTimeOffset.UtcNow;

        // Create a return record
        var returnIssue = new MaterialIssue
        {
            JobId = original.JobId,
            PartId = original.PartId,
            OperationId = original.OperationId,
            Quantity = original.Quantity,
            UnitCost = original.UnitCost,
            IssuedById = request.ReturnedById,
            IssuedAt = now,
            BinContentId = original.BinContentId,
            StorageLocationId = original.StorageLocationId,
            LotNumber = original.LotNumber,
            IssueType = MaterialIssueType.Return,
            ReturnReasonId = null,
            Notes = MaterialIssueReturn.NoteFor(original.Id),
        };

        db.MaterialIssues.Add(returnIssue);

        if (!request.StockRestoredByCaller)
        {
            var restored = await RestoreStockAsync(original, request.ReturnedById, now, cancellationToken);
            if (restored is not null)
                returnIssue.BinContent = restored;
        }

        await db.SaveChangesAsync(cancellationToken);

        if (posting is not null)
        {
            var entryDate = DateOnly.FromDateTime(returnIssue.IssuedAt.UtcDateTime);
            await posting.PostMaterialIssueAsync(returnIssue.Id, entryDate, request.ReturnedById, cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

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

    private async Task<BinContent?> RestoreStockAsync(
        MaterialIssue original, int userId, DateTimeOffset now, CancellationToken ct)
    {
        var bin = original.BinContentId is int binId
            ? await db.BinContents.FindAsync([binId], ct)
            : null;

        BinContent row;
        if (bin is not null && bin.RemovedAt is null)
        {
            bin.Quantity += original.Quantity;
            row = bin;
        }
        else if ((bin?.LocationId ?? original.StorageLocationId) is int locationId)
        {
            row = await ScanBinStock.AddAsync(
                db, original.PartId, locationId, bin?.LotNumber ?? original.LotNumber, original.Quantity, userId, now, ct);
        }
        else
        {
            return null;
        }

        if (row.Status == BinContentStatus.Stored
            && await LotQualityHold.IsHeldAsync(db, original.PartId, row.LotNumber, ct))
            row.Status = BinContentStatus.QcHold;

        db.BinMovements.Add(new BinMovement
        {
            EntityType = "part",
            EntityId = original.PartId,
            Quantity = original.Quantity,
            LotNumber = row.LotNumber,
            ToLocationId = row.LocationId,
            Reason = BinMovementReason.Adjustment,
            MovedBy = userId,
            MovedAt = now,
        });
        return row;
    }
}
