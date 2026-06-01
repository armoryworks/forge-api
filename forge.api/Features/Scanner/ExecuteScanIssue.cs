using System.Security.Claims;

using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

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
        var sourceContent = await db.BinContents
            .Where(bc => bc.EntityType == "part"
                && bc.EntityId == data.PartId
                && bc.LocationId == data.FromLocationId
                && bc.Quantity > 0
                && bc.RemovedAt == null)
            .FirstOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException(
                $"No stock found for part {part.PartNumber} at source location");

        if (sourceContent.Quantity < data.Quantity)
            throw new InvalidOperationException(
                $"Cannot issue {data.Quantity} — only {sourceContent.Quantity} available");

        // Decrement source
        sourceContent.Quantity -= data.Quantity;
        if (sourceContent.Quantity == 0)
        {
            sourceContent.RemovedAt = now;
            sourceContent.RemovedBy = userId;
        }

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

        // Create bin movement
        var movement = new BinMovement
        {
            EntityType = "part",
            EntityId = data.PartId,
            Quantity = data.Quantity,
            LotNumber = sourceContent.LotNumber,
            FromLocationId = data.FromLocationId,
            MovedBy = userId,
            MovedAt = now,
            Reason = BinMovementReason.ScanIssue,
            ScanActionLogId = scanLog.Id,
        };
        db.BinMovements.Add(movement);
        await db.SaveChangesAsync(cancellationToken);

        return scanLog.Id;
    }
}
