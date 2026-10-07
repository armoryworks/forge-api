using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.Jobs;

public record ExplodeJobBomCommand(int JobId) : IRequest<BomExplosionResponseModel>;

public class ExplodeJobBomValidator : AbstractValidator<ExplodeJobBomCommand>
{
    public ExplodeJobBomValidator()
    {
        RuleFor(x => x.JobId).GreaterThan(0);
    }
}

public class ExplodeJobBomHandler(
    AppDbContext db,
    IJobRepository jobRepo,
    IBarcodeService barcodeService,
    IHubContext<BoardHub> boardHub) : IRequestHandler<ExplodeJobBomCommand, BomExplosionResponseModel>
{
    public async Task<BomExplosionResponseModel> Handle(ExplodeJobBomCommand request, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);

        if (db.Database.IsNpgsql())
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM jobs WHERE id = {request.JobId} FOR UPDATE", ct);

        var parentJob = await db.Jobs
            .Include(j => j.TrackType)
                .ThenInclude(t => t.Stages.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found.");

        if (await db.Jobs.AnyAsync(j => j.ParentJobId == parentJob.Id && j.DeletedAt == null, ct))
            throw new InvalidOperationException("This work order has already been exploded.");

        if (!parentJob.PartId.HasValue)
            throw new InvalidOperationException("This work order has no part. Pick a part, then explode the BOM.");

        var part = await db.Parts
            .FirstOrDefaultAsync(p => p.Id == parentJob.PartId.Value, ct)
            ?? throw new KeyNotFoundException($"Part {parentJob.PartId.Value} not found.");

        var bomLines = await LoadBomLinesAsync(parentJob, part, ct);

        if (bomLines.Count == 0)
            throw new InvalidOperationException(
                $"Part {part.PartNumber} has no BOM lines to explode. Add BOM lines to the part first.");

        var firstStage = parentJob.TrackType.Stages.FirstOrDefault()
            ?? throw new InvalidOperationException(
                $"Order type '{parentJob.TrackType.Name}' has no statuses. Add statuses in Admin.");

        var buildQty = await db.Set<JobPart>()
            .Where(jp => jp.JobId == parentJob.Id && jp.PartId == parentJob.PartId.Value)
            .SumAsync(jp => jp.Quantity, ct);
        if (buildQty <= 0)
            buildQty = 1;

        var newChildJobs = new List<(Job Job, Part Part, decimal Quantity)>();
        var buyItems = new List<BomExplosionBuyItemModel>();
        var stockItems = new List<BomExplosionStockItemModel>();

        foreach (var bomLine in bomLines)
        {
            var childPart = bomLine.ChildPart;
            var required = RequiredQuantity(bomLine, buildQty);

            switch (bomLine.SourceType)
            {
                case BOMSourceType.Make:
                {
                    var jobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
                    var maxPos = await jobRepo.GetMaxBoardPositionAsync(firstStage.Id, ct);

                    var childJob = new Job
                    {
                        JobNumber = jobNumber,
                        Title = childPart.Description ?? childPart.Name,
                        TrackTypeId = parentJob.TrackTypeId,
                        CurrentStageId = firstStage.Id,
                        CustomerId = parentJob.CustomerId,
                        BoardPosition = maxPos + 1,
                        PartId = childPart.Id,
                        ParentJobId = parentJob.Id,
                        Priority = parentJob.Priority,
                        DueDate = parentJob.DueDate,
                    };

                    await jobRepo.AddAsync(childJob, ct);

                    // Bidirectional links + JobPart reference the child through
                    // navigation properties: the child's id doesn't exist until
                    // SaveChanges, and raw-int FKs captured a 0 here (FK
                    // violation on Postgres — the reported "button does nothing").
                    db.Set<JobLink>().Add(new JobLink
                    {
                        SourceJobId = parentJob.Id,
                        TargetJob = childJob,
                        LinkType = JobLinkType.Parent,
                    });

                    db.Set<JobLink>().Add(new JobLink
                    {
                        SourceJob = childJob,
                        TargetJobId = parentJob.Id,
                        LinkType = JobLinkType.Child,
                    });

                    db.Set<JobPart>().Add(new JobPart
                    {
                        Job = childJob,
                        PartId = childPart.Id,
                        Quantity = required,
                    });

                    childJob.ActivityLogs.Add(new JobActivityLog
                    {
                        Action = ActivityAction.Created,
                        Description = $"Job {jobNumber} created from the BOM of job {parentJob.JobNumber}.",
                    });

                    newChildJobs.Add((childJob, childPart, required));
                    break;
                }

                case BOMSourceType.Buy:
                    buyItems.Add(new BomExplosionBuyItemModel(
                        childPart.Id,
                        childPart.PartNumber,
                        childPart.Description ?? childPart.Name,
                        required,
                        childPart.PreferredVendorId,
                        childPart.PreferredVendor?.CompanyName,
                        bomLine.LeadTimeDays,
                        parentJob.DueDate?.AddDays(-(bomLine.LeadTimeDays ?? 0))));
                    break;

                case BOMSourceType.Stock:
                {
                    var needed = required;
                    var reserved = 0m;

                    // Auto-reserve available stock across bins (oldest first)
                    var bins = await db.BinContents
                        .Where(b => b.EntityType == "part"
                            && b.EntityId == childPart.Id
                            && b.RemovedAt == null
                            && (b.Quantity - b.ReservedQuantity) > 0)
                        .OrderBy(b => b.PlacedAt)
                        .ToListAsync(ct);

                    foreach (var bin in bins)
                    {
                        if (reserved >= needed) break;

                        var available = bin.Quantity - bin.ReservedQuantity;
                        var toReserve = Math.Min(available, needed - reserved);

                        db.Set<Reservation>().Add(new Reservation
                        {
                            PartId = childPart.Id,
                            BinContentId = bin.Id,
                            JobId = parentJob.Id,
                            Quantity = toReserve,
                            Notes = $"Auto-reserved via BOM explosion for job {parentJob.JobNumber}",
                        });

                        bin.ReservedQuantity += toReserve;
                        reserved += toReserve;
                    }

                    stockItems.Add(new BomExplosionStockItemModel(
                        childPart.Id,
                        childPart.PartNumber,
                        childPart.Description ?? childPart.Name,
                        needed,
                        reserved,
                        reserved < needed));
                    break;
                }
            }
        }

        parentJob.ActivityLogs.Add(new JobActivityLog
        {
            Action = ActivityAction.BomExploded,
            Description = $"BOM exploded for {buildQty:0.####}: {newChildJobs.Count} sub-jobs, {buyItems.Count} buy lines, {stockItems.Count} stock lines.",
        });

        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);

        // Response models are built AFTER the save so they carry the real
        // database-assigned child ids (building them earlier captured 0s).
        var createdJobs = new List<BomExplosionChildJobModel>(newChildJobs.Count);

        // Give each child job a scannable barcode and announce it to the live
        // board — without the broadcast the board behind the explode dialog
        // never refreshed and the whole explosion looked like a no-op.
        foreach (var (childJob, childPart, quantity) in newChildJobs)
        {
            createdJobs.Add(new BomExplosionChildJobModel(
                childJob.Id,
                childJob.JobNumber,
                childJob.Title,
                childPart.Id,
                childPart.PartNumber,
                quantity,
                childJob.DueDate));

            await barcodeService.CreateBarcodeAsync(
                BarcodeEntityType.Job, childJob.Id, childJob.JobNumber, ct);

            await boardHub.Clients.Group($"board:{parentJob.TrackTypeId}")
                .SendAsync("jobCreated", new BoardJobCreatedEvent(
                    childJob.Id, childJob.JobNumber, childJob.Title, parentJob.TrackTypeId,
                    firstStage.Id, firstStage.Name, childJob.BoardPosition), ct);
        }

        return new BomExplosionResponseModel(
            parentJob.Id,
            createdJobs,
            buyItems,
            stockItems);
    }

    private async Task<List<BomExplosionLine>> LoadBomLinesAsync(
        Job parentJob, Part part, CancellationToken ct)
    {
        if (parentJob.BomRevisionIdAtRelease is int revisionId)
        {
            var entries = await db.Set<BomRevisionLine>()
                .Include(e => e.Part)
                    .ThenInclude(p => p.PreferredVendor)
                .Include(e => e.Part)
                    .ThenInclude(p => p.StockUom)
                .Where(e => e.BomRevisionId == revisionId)
                .OrderBy(e => e.SortOrder)
                .ToListAsync(ct);

            return entries
                .Select(e => new BomExplosionLine(e.Part, e.Quantity, e.SourceType, e.LeadTimeDays, e.UnitOfMeasure))
                .ToList();
        }

        var lines = await db.BOMLines
            .Include(b => b.ChildPart)
                .ThenInclude(cp => cp.PreferredVendor)
            .Include(b => b.ChildPart)
                .ThenInclude(cp => cp.StockUom)
            .Include(b => b.Uom)
            .Where(b => b.ParentPartId == part.Id)
            .OrderBy(b => b.SortOrder)
            .ToListAsync(ct);

        return lines
            .Select(b => new BomExplosionLine(b.ChildPart, b.Quantity, b.SourceType, b.LeadTimeDays, b.Uom?.Code))
            .ToList();
    }

    private static decimal RequiredQuantity(BomExplosionLine line, decimal buildQty)
    {
        var required = line.Quantity * buildQty;
        return IsEach(line.ChildPart.StockUom, line.LineUom) ? Math.Ceiling(required) : required;
    }

    private static bool IsEach(UnitOfMeasure? stockUom, string? lineUom)
    {
        if (stockUom is not null)
            return IsEachName(stockUom.Code) || IsEachName(stockUom.Name);

        return IsEachName(lineUom);
    }

    private static bool IsEachName(string? uom)
    {
        var value = uom?.Trim();
        return string.Equals(value, "ea", StringComparison.OrdinalIgnoreCase)
            || string.Equals(value, "each", StringComparison.OrdinalIgnoreCase);
    }
}
