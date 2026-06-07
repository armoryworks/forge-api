using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Accounting;
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

public class CreateMaterialIssueHandler(
    AppDbContext db,
    // Phase-2 STAGE E — optional / null-default so the handler stays constructible without an accounting
    // context (isolated unit tests). Production DI supplies it; with CAP-ACCT-FULLGL off it no-ops.
    IMaterialIssuePostingService? posting = null)
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

        // One transaction: the issue + bin decrement AND the inline WIP / inventory posting commit (or roll
        // back) together — the locked inline model. The engine's SaveChanges joins this transaction; a posting
        // failure (FULLGL on) unwinds the issue too. On Npgsql this is a real transaction; the in-memory test
        // provider treats it as an ignored no-op. tx is opened only when posting is wired.
        await using var tx = posting is not null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await db.SaveChangesAsync(cancellationToken);

        // Inline WIP / inventory posting (Phase-2 STAGE E) — no-op while CAP-ACCT-FULLGL is off. Runs after the
        // issue is flushed so its id resolves; relieves the perpetual valuation store at weighted-average.
        if (posting is not null)
        {
            var entryDate = DateOnly.FromDateTime(issue.IssuedAt.UtcDateTime);
            await posting.PostMaterialIssueAsync(issue.Id, entryDate, request.IssuedById, cancellationToken);
        }

        if (tx is not null)
            await tx.CommitAsync(cancellationToken);

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
