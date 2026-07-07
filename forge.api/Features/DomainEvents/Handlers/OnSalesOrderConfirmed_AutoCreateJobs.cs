using MediatR;

using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;

using Forge.Api.Hubs;
using Forge.Core.Entities;
using Forge.Core.Enums;
using Forge.Core.Interfaces;
using Forge.Core.Models;
using Forge.Data.Context;

namespace Forge.Api.Features.DomainEvents.Handlers;

public class OnSalesOrderConfirmed_AutoCreateJobs(
    AppDbContext db,
    IJobRepository jobRepo,
    ITrackTypeRepository trackRepo,
    IBarcodeService barcodeService,
    IHubContext<BoardHub> boardHub,
    ILogger<OnSalesOrderConfirmed_AutoCreateJobs> logger)
    : INotificationHandler<SalesOrderConfirmedEvent>
{
    public async Task Handle(SalesOrderConfirmedEvent notification, CancellationToken ct)
    {
        var so = await db.SalesOrders
            .Include(s => s.Lines)
                .ThenInclude(l => l.Part)
            .Include(s => s.Customer)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == notification.SalesOrderId, ct);

        if (so is null)
        {
            logger.LogWarning("SalesOrder {Id} not found for auto-job creation", notification.SalesOrderId);
            return;
        }

        // Find the default Production track type
        var productionTrack = await db.TrackTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.IsDefault && t.IsActive, ct);

        if (productionTrack is null)
        {
            productionTrack = await db.TrackTypes
                .AsNoTracking()
                .FirstOrDefaultAsync(t => t.IsActive, ct);
        }

        if (productionTrack is null)
        {
            logger.LogError("No active TrackType found — cannot auto-create jobs for SO {OrderNumber}", so.OrderNumber);
            return;
        }

        // A job born from a *confirmed* order starts at the order_confirmed
        // stage — the Sales Orders surface's entry point — not at the quote
        // stages it never went through. Starting below order_confirmed made a
        // freshly-confirmed SO invisible on the Sales Orders list (the Draft
        // block excludes it and the Job projection didn't see it yet). Custom
        // tracks without that stage code fall back to the first active stage.
        var startStage = await db.JobStages.AsNoTracking()
            .Where(s => s.TrackTypeId == productionTrack.Id && s.IsActive && s.Code == "order_confirmed")
            .OrderBy(s => s.SortOrder)
            .FirstOrDefaultAsync(ct)
            ?? await trackRepo.FindFirstActiveStageAsync(productionTrack.Id, ct);
        if (startStage is null)
        {
            logger.LogError("No active stages for TrackType {TrackTypeId} — cannot auto-create jobs for SO {OrderNumber}",
                productionTrack.Id, so.OrderNumber);
            return;
        }

        // Find lines that don't already have jobs
        var lineIds = so.Lines.Select(l => l.Id).ToList();
        var linesWithJobs = await db.Jobs
            .Where(j => j.SalesOrderLineId.HasValue && lineIds.Contains(j.SalesOrderLineId.Value))
            .Select(j => j.SalesOrderLineId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var linesWithJobsSet = linesWithJobs.ToHashSet();
        var linesToProcess = so.Lines.Where(l => !linesWithJobsSet.Contains(l.Id)).ToList();

        if (linesToProcess.Count == 0)
        {
            logger.LogInformation("All lines for SO {OrderNumber} already have jobs — skipping", so.OrderNumber);
            return;
        }

        var createdJobs = new List<(Job Job, JobStage Stage)>();

        foreach (var line in linesToProcess)
        {
            var jobNumber = await jobRepo.GenerateNextJobNumberAsync(ct);
            var maxPosition = await jobRepo.GetMaxBoardPositionAsync(startStage.Id, ct);

            var title = !string.IsNullOrWhiteSpace(line.Part?.Description)
                ? line.Part.Description
                : $"Job for SO-{so.OrderNumber} Line {line.LineNumber}";

            var job = new Job
            {
                JobNumber = jobNumber,
                Title = title,
                Description = $"Auto-created from Sales Order {so.OrderNumber}, Line {line.LineNumber}. Qty: {line.Quantity}.",
                TrackTypeId = productionTrack.Id,
                CurrentStageId = startStage.Id,
                SalesOrderLineId = line.Id,
                PartId = line.PartId,
                CustomerId = so.CustomerId,
                Priority = JobPriority.Normal,
                DueDate = so.RequestedDeliveryDate,
                BoardPosition = maxPosition + 1,
            };

            job.ActivityLogs.Add(new JobActivityLog
            {
                Action = ActivityAction.Created,
                Description = $"Job {jobNumber} auto-created from confirmed Sales Order {so.OrderNumber}, Line {line.LineNumber}.",
            });

            await jobRepo.AddAsync(job, ct);
            createdJobs.Add((job, startStage));
        }

        await jobRepo.SaveChangesAsync(ct);

        // Create barcodes and broadcast to board for each new job
        foreach (var (job, stage) in createdJobs)
        {
            await barcodeService.CreateBarcodeAsync(
                BarcodeEntityType.Job, job.Id, job.JobNumber, ct);

            await boardHub.Clients.Group($"board:{productionTrack.Id}")
                .SendAsync("jobCreated", new BoardJobCreatedEvent(
                    job.Id, job.JobNumber, job.Title, productionTrack.Id,
                    stage.Id, stage.Name, job.BoardPosition), ct);
        }

        // Log activity on the SO
        db.ActivityLogs.Add(new ActivityLog
        {
            EntityType = "SalesOrder",
            EntityId = so.Id,
            UserId = notification.UserId,
            Action = "jobs_auto_created",
            Description = $"{createdJobs.Count} production job(s) auto-created for Sales Order {so.OrderNumber}.",
        });

        await db.SaveChangesAsync(ct);

        logger.LogInformation("Auto-created {Count} job(s) for confirmed SO {OrderNumber}",
            createdJobs.Count, so.OrderNumber);
    }
}
