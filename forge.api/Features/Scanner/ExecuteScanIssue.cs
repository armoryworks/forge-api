using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Features.Inventory;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Scanner;

public record ExecuteScanIssueCommand(ScanIssueRequestModel Data) : IRequest<int>;

public class ExecuteScanIssueCommandValidator : AbstractValidator<ExecuteScanIssueCommand>
{
    public ExecuteScanIssueCommandValidator()
    {
        RuleFor(x => x.Data.PartId).GreaterThan(0);
        RuleFor(x => x.Data.JobId).GreaterThan(0);
        RuleFor(x => x.Data.Quantity).GreaterThan(0);
        RuleFor(x => x.Data.FromLocationId).GreaterThan(0);
    }
}

public class ExecuteScanIssueHandler(
    AppDbContext db,
    IClock clock,
    IHttpContextAccessor httpContext)
    : IRequestHandler<ExecuteScanIssueCommand, int>
{
    public async Task<int> Handle(ExecuteScanIssueCommand request, CancellationToken cancellationToken)
    {
        var data = request.Data;
        var userId = int.Parse(httpContext.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
        var now = clock.UtcNow;

        // Validate job exists and is active
        var job = await db.Jobs.AsNoTracking()
            .Where(j => j.Id == data.JobId && !j.IsArchived && j.CompletedDate == null)
            .Select(j => new { j.Id, j.JobNumber, j.PartId })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Active job {data.JobId} not found");

        // Validate part exists
        var part = await db.Parts.AsNoTracking()
            .Where(p => p.Id == data.PartId)
            .Select(p => new { p.Id, p.PartNumber })
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException($"Part {data.PartId} not found");

        // Validate part is in BOM for the job's part (or any related BOM)
        if (job.PartId.HasValue)
        {
            var isInBom = await db.BOMLines.AsNoTracking()
                .AnyAsync(bom => bom.ParentPartId == job.PartId && bom.ChildPartId == data.PartId,
                    cancellationToken);

            if (!isInBom)
                throw new InvalidOperationException(
                    $"Part {part.PartNumber} is not in the BOM for job {job.JobNumber}");
        }

        // Validate source has sufficient stock
        var sourceRows = await ScanBinStock.DrawableRowsAsync(
            db, data.PartId, data.FromLocationId, data.Quantity, cancellationToken);
        if (!sourceRows.Any(r => r.Quantity > 0))
            throw new KeyNotFoundException(
                $"No stock found for part {part.PartNumber} at source location");

        var available = sourceRows.Sum(r => r.Quantity - r.ReservedQuantity);
        if (available < data.Quantity)
            throw new InvalidOperationException(
                $"Cannot issue {data.Quantity} — only {available} available");

        var drawn = BinContentDrawDown.Take(sourceRows, data.Quantity, userId, now);

        // Create scan action log
        var scanLog = new ScanActionLog
        {
            UserId = userId,
            ActionType = ScanActionType.Issue,
            PartId = data.PartId,
            PartNumber = part.PartNumber,
            FromLocationId = data.FromLocationId,
            Quantity = data.Quantity,
            RelatedEntityId = data.JobId,
            RelatedEntityType = "Job",
        };
        db.ScanActionLogs.Add(scanLog);
        await db.SaveChangesAsync(cancellationToken);

        foreach (var (row, taken) in drawn)
        {
            db.BinMovements.Add(new BinMovement
            {
                EntityType = "part",
                EntityId = data.PartId,
                Quantity = taken,
                LotNumber = row.LotNumber,
                FromLocationId = data.FromLocationId,
                MovedBy = userId,
                MovedAt = now,
                Reason = BinMovementReason.ScanIssue,
                ScanActionLogId = scanLog.Id,
            });
        }
        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
