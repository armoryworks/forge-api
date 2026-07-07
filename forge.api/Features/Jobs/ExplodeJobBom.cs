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
        var parentJob = await db.Jobs
            .Include(j => j.TrackType)
                .ThenInclude(t => t.Stages.OrderBy(s => s.SortOrder))
            .FirstOrDefaultAsync(j => j.Id == request.JobId, ct)
            ?? throw new KeyNotFoundException($"Job {request.JobId} not found.");

        if (!parentJob.PartId.HasValue)
            throw new InvalidOperationException($"Job {request.JobId} has no associated part. Set PartId before exploding BOM.");

        var part = await db.Parts
            .Include(p => p.BOMLines)
                .ThenInclude(b => b.ChildPart)
                    .ThenInclude(cp => cp.PreferredVendor)
            .FirstOrDefaultAsync(p => p.Id == parentJob.PartId.Value, ct)
            ?? throw new KeyNotFoundException($"Part {parentJob.PartId.Value} not found.");

        if (part.BOMLines.Count == 0)
            throw new InvalidOperationException(
                $"Part {part.PartNumber} has no BOM lines to explode. Add BOM lines to the part first.");

        var firstStage = parentJob.TrackType.Stages.FirstOrDefault()
            ?? throw new InvalidOperationException($"Track type '{parentJob.TrackType.Name}' has no stages configured.");

        var newChildJobs = new List<(Job Job, Part Part, decimal Quantity)>();
        var buyItems = new List<BomExplosionBuyItemModel>();
        var stockItems = new List<BomExplosionStockItemModel>();

        foreach (var bomLine in part.BOMLines.OrderBy(b => b.SortOrder))
        {
            var childPart = bomLine.ChildPart;

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
                        Quantity = bomLine.Quantity,
                    });

                    newChildJobs.Add((childJob, childPart, bomLine.Quantity));
                    break;
                }

                case BOMSourceType.Buy:
                    buyItems.Add(new BomExplosionBuyItemModel(
                        childPart.Id,
                        childPart.PartNumber,
                        childPart.Description ?? childPart.Name,
                        bomLine.Quantity,
                        childPart.PreferredVendorId,
                        childPart.PreferredVendor?.CompanyName,
                        bomLine.LeadTimeDays));
                    break;

                case BOMSourceType.Stock:
                {
                    var needed = bomLine.Quantity;
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

        await db.SaveChangesAsync(ct);

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
                quantity));

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
}
